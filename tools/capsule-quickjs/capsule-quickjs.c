// Host-owned QuickJS carrier. Addons supply JavaScript only; no WASI or CLR API is exposed to them.
// Built against QuickJS-NG v0.17.0 (6d46d07d04041b40f4f49eaa7fdebe44c314c699).
#include "quickjs.h"
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <stdio.h>

__attribute__((import_module("portable_ui_v1"), import_name("ui_reserve_handle")))
extern int32_t host_reserve(uint64_t *output);
__attribute__((import_module("portable_ui_v1"), import_name("ui_submit")))
extern int32_t host_submit(const uint8_t *bytes, int32_t length);
__attribute__((import_module("portable_ui_v1"), import_name("ui_next_event")))
extern int32_t host_next_event(uint8_t *bytes, int32_t capacity);
__attribute__((import_module("portable_ui_v1"), import_name("game_next_observation")))
extern int32_t host_next_observation(uint8_t *bytes, int32_t capacity);
__attribute__((import_module("portable_ui_v1"), import_name("game_request_action")))
extern int32_t host_request_action(uint64_t handle, int32_t capability,
                                   const uint8_t *bytes, int32_t length);
__attribute__((import_module("portable_ui_v1"), import_name("package_read_data")))
extern int32_t host_read_data(const char *key, int32_t key_length,
                              uint8_t *output, int32_t capacity);
__attribute__((import_module("portable_ui_v1"), import_name("package_acquire_image")))
extern int32_t host_acquire_image(const char *key, int32_t key_length,
                                  char *output, int32_t capacity);

static JSRuntime *runtime;
static JSContext *context;
static char last_error[512];

static void capture_error(void) {
    JSValue exception = JS_GetException(context);
    JSValue stack = JS_GetPropertyStr(context, exception, "stack");
    const char *message = JS_ToCString(context, exception);
    if (message) {
        size_t length = strlen(message);
        if (length >= sizeof(last_error)) length = sizeof(last_error) - 1;
        memcpy(last_error, message, length);
        last_error[length] = 0;
        JS_FreeCString(context, message);
    } else {
        strcpy(last_error, "JavaScript exception");
    }
    if (JS_IsString(stack)) {
        const char *trace = JS_ToCString(context, stack);
        if (trace) {
            size_t used = strlen(last_error);
            if (used + 1 < sizeof(last_error)) {
                last_error[used++] = '\n';
                size_t remaining = sizeof(last_error) - used - 1;
                size_t length = strlen(trace);
                if (length > remaining) length = remaining;
                memcpy(last_error + used, trace, length);
                last_error[used + length] = 0;
            }
            JS_FreeCString(context, trace);
        }
    }
    JS_FreeValue(context, stack);
    JS_FreeValue(context, exception);
}

static int bytes_arg(JSContext *ctx, JSValueConst value, const uint8_t **data, int32_t *length) {
    size_t offset = 0, count = 0, stride = 0, total = 0;
    JSValue buffer = JS_GetTypedArrayBuffer(ctx, value, &offset, &count, &stride);
    if (JS_IsException(buffer)) return -1;
    uint8_t *base = JS_GetArrayBuffer(ctx, &total, buffer);
    int ok = base && stride == 1 && count <= 65536 && offset <= total && count <= total - offset;
    if (ok) { *data = base + offset; *length = (int32_t)count; }
    JS_FreeValue(ctx, buffer);
    return ok ? 0 : -1;
}

static JSValue js_reserve(JSContext *ctx, JSValueConst self, int argc, JSValueConst *argv) {
    (void)self; (void)argc; (void)argv;
    uint64_t handle = 0;
    int32_t result = host_reserve(&handle);
    if (result != 0) return JS_ThrowInternalError(ctx, "reserve rejected: %d", result);
    return JS_NewBigUint64(ctx, handle);
}

static JSValue js_submit(JSContext *ctx, JSValueConst self, int argc, JSValueConst *argv) {
    (void)self;
    const uint8_t *data; int32_t length;
    if (argc != 1 || bytes_arg(ctx, argv[0], &data, &length) != 0)
        return JS_ThrowTypeError(ctx, "submit expects a Uint8Array of at most 65536 bytes");
    int32_t result = host_submit(data, length);
    if (result != 0) return JS_ThrowInternalError(ctx, "submit rejected: %d", result);
    return JS_UNDEFINED;
}

static JSValue next_bytes(JSContext *ctx, int32_t (*next)(uint8_t *, int32_t), int32_t capacity) {
    uint8_t buffer[1052];
    int32_t count = next(buffer, capacity);
    if (count < 0 || count > capacity)
        return JS_ThrowInternalError(ctx, "queued message rejected: %d", count);
    return JS_NewUint8ArrayCopy(ctx, buffer, (size_t)count);
}

static JSValue js_next_event(JSContext *ctx, JSValueConst self, int argc, JSValueConst *argv) {
    (void)self; (void)argc; (void)argv;
    return next_bytes(ctx, host_next_event, 1052);
}

static void *observation_realloc(JSRuntime *rt, void *opaque, void *ptr, size_t size) {
    (void)opaque;
    return js_realloc_rt(rt, ptr, size);
}
static JSValue js_next_observation(JSContext *ctx, JSValueConst self, int argc, JSValueConst *argv) {
    (void)self; (void)argc; (void)argv;
    int32_t capacity = host_next_observation(NULL, 0);
    // Older hosts reject the optional length query and keep their legacy path.
    if (capacity == -2) return next_bytes(ctx, host_next_observation, 264);
    if (capacity == 0) return JS_NewUint8ArrayCopy(ctx, NULL, 0);
    if (capacity < 8 || capacity > 4194312)
        return JS_ThrowInternalError(ctx, "observation length rejected: %d", capacity);
    uint8_t *buffer = js_malloc(ctx, (size_t)capacity);
    if (!buffer) return JS_EXCEPTION;
    int32_t count = host_next_observation(buffer, capacity);
    if (count != capacity) {
        js_free(ctx, buffer);
        return JS_ThrowInternalError(ctx, "observation changed during delivery: %d", count);
    }
    return JS_NewUint8Array(ctx, buffer, (size_t)count, observation_realloc, NULL, false);
}

static JSValue js_request_action(JSContext *ctx, JSValueConst self, int argc, JSValueConst *argv) {
    (void)self;
    uint64_t handle; int32_t capability; const uint8_t *data; int32_t length;
    if (argc != 3 || !JS_IsBigInt(argv[0]) || JS_ToBigUint64(ctx, &handle, argv[0]) < 0 ||
        JS_ToInt32(ctx, &capability, argv[1]) < 0 || capability < 1 || capability > 65535 ||
        bytes_arg(ctx, argv[2], &data, &length) != 0 || length > 4194304)
        return JS_ThrowTypeError(ctx, "invalid action request");
    int32_t result = host_request_action(handle, capability, data, length);
    if (result != 0) return JS_ThrowInternalError(ctx, "action denied: %d", result);
    return JS_UNDEFINED;
}

static JSValue js_read_data(JSContext *ctx, JSValueConst self, int argc, JSValueConst *argv) {
    (void)self;
    size_t key_length; const char *key;
    if (argc != 1 || !JS_IsString(argv[0]) || !(key = JS_ToCStringLen(ctx, &key_length, argv[0])))
        return JS_ThrowTypeError(ctx, "data key must be a string");
    if (key_length == 0 || key_length > 96) {
        JS_FreeCString(ctx, key); return JS_ThrowRangeError(ctx, "data key length invalid");
    }
    uint8_t *buffer = malloc(65536);
    if (!buffer) { JS_FreeCString(ctx, key); return JS_ThrowOutOfMemory(ctx); }
    int32_t count = host_read_data(key, (int32_t)key_length, buffer, 65536);
    JS_FreeCString(ctx, key);
    JSValue result = count >= 0 && count <= 65536 ?
        JS_NewUint8ArrayCopy(ctx, buffer, (size_t)count) :
        JS_ThrowInternalError(ctx, "package data unavailable");
    free(buffer);
    return result;
}

static JSValue js_acquire_image(JSContext *ctx, JSValueConst self, int argc, JSValueConst *argv) {
    (void)self;
    size_t key_length; const char *key;
    if (argc != 1 || !JS_IsString(argv[0]) || !(key = JS_ToCStringLen(ctx, &key_length, argv[0])))
        return JS_ThrowTypeError(ctx, "image key must be a string");
    if (key_length == 0 || key_length > 96) {
        JS_FreeCString(ctx, key); return JS_ThrowRangeError(ctx, "image key length invalid");
    }
    char output[96];
    int32_t count = host_acquire_image(key, (int32_t)key_length, output, 96);
    JS_FreeCString(ctx, key);
    if (count < 0 || count > 96) return JS_ThrowInternalError(ctx, "package image unavailable");
    return JS_NewStringLen(ctx, output, (size_t)count);
}

__attribute__((export_name("portable_ui_alloc")))
void *portable_ui_alloc(int32_t length) {
    return length > 0 && length <= 524289 ? malloc((size_t)length) : NULL;
}

__attribute__((export_name("portable_ui_free")))
void portable_ui_free(void *pointer) { free(pointer); }

__attribute__((export_name("portable_ui_init")))
int32_t portable_ui_init(void) {
    if (runtime) return -1;
    last_error[0] = 0;
    runtime = JS_NewRuntime();
    if (!runtime) return -2;
    JS_SetMemoryLimit(runtime, 4 * 1024 * 1024);
    JS_SetMaxStackSize(runtime, 128 * 1024);
    context = JS_NewContext(runtime);
    if (!context) return -3;
    JSValue global = JS_GetGlobalObject(context);
    JSValue native = JS_NewObject(context);
    if (JS_IsException(global) || JS_IsException(native) ||
        JS_SetPropertyStr(context, native, "reserve", JS_NewCFunction(context, js_reserve, "reserve", 0)) < 0 ||
        JS_SetPropertyStr(context, native, "submit", JS_NewCFunction(context, js_submit, "submit", 1)) < 0 ||
        JS_SetPropertyStr(context, native, "nextEvent", JS_NewCFunction(context, js_next_event, "nextEvent", 0)) < 0 ||
        JS_SetPropertyStr(context, native, "nextObservation", JS_NewCFunction(context, js_next_observation, "nextObservation", 0)) < 0 ||
        JS_SetPropertyStr(context, native, "requestAction", JS_NewCFunction(context, js_request_action, "requestAction", 3)) < 0 ||
        JS_SetPropertyStr(context, native, "readData", JS_NewCFunction(context, js_read_data, "readData", 1)) < 0 ||
        JS_SetPropertyStr(context, native, "acquireImage", JS_NewCFunction(context, js_acquire_image, "acquireImage", 1)) < 0 ||
        JS_SetPropertyStr(context, global, "portableUi", JS_DupValue(context, native)) < 0) {
        JS_FreeValue(context, native);
        JS_FreeValue(context, global);
        capture_error();
        return -4;
    }
    JS_FreeValue(context, native);
    JS_FreeValue(context, global);
    return 0;
}

static int32_t evaluate(const char *source, int32_t length, const char *name) {
    if (!context || !source || length < 0 || length > 524288) return -1;
    last_error[0] = 0;
    JSValue result = JS_Eval(context, source, (size_t)length, name, JS_EVAL_TYPE_GLOBAL);
    if (JS_IsException(result)) { capture_error(); JS_FreeValue(context, result); return -2; }
    JS_FreeValue(context, result);
    return 0;
}

__attribute__((export_name("portable_ui_bootstrap")))
int32_t portable_ui_bootstrap(const char *source, int32_t length) {
    return evaluate(source, length, "portable-ui-sdk.js");
}

__attribute__((export_name("portable_ui_eval")))
int32_t portable_ui_eval(const char *source, int32_t length) {
    return evaluate(source, length, "bundle.js");
}

__attribute__((export_name("portable_ui_update")))
int32_t portable_ui_update(void) {
    if (!context) return -1;
    last_error[0] = 0;
    JSValue global = JS_GetGlobalObject(context);
    JSValue callback = JS_GetPropertyStr(context, global, "portableUiUpdate");
    JSValue result = JS_Call(context, callback, global, 0, NULL);
    JS_FreeValue(context, callback);
    JS_FreeValue(context, global);
    if (JS_IsException(result)) { capture_error(); JS_FreeValue(context, result); return -2; }
    JS_FreeValue(context, result);
    // Bounded microtask progress; remaining jobs wait for the next host update.
    for (int i = 0; i < 16 && JS_IsJobPending(runtime); ++i) {
        JSContext *job_context = NULL;
        if (JS_ExecutePendingJob(runtime, &job_context) < 0) { capture_error(); return -3; }
    }
    return 0;
}

// Optional scheduling ABI v2: zero=no work, positive=delay milliseconds+1.
// Legacy update semantics stay unchanged. The host meters this entire export,
// including the bounded job drain and scheduling query, under one update budget.
__attribute__((export_name("portable_ui_update_v2")))
int32_t portable_ui_update_v2(void) {
    int32_t status = portable_ui_update();
    if (status < 0) return status;
    JSValue global = JS_GetGlobalObject(context);
    JSValue callback = JS_GetPropertyStr(context, global, "__portableNextDelayV1");
    JSValue result = JS_Call(context, callback, global, 0, NULL);
    JS_FreeValue(context, callback);
    JS_FreeValue(context, global);
    if (JS_IsException(result)) { capture_error(); JS_FreeValue(context, result); return -4; }
    double delay;
    int converted = JS_ToFloat64(context, &delay, result);
    JS_FreeValue(context, result);
    if (converted < 0 || !isfinite(delay) || delay < -1 || (delay < 0 && delay != -1)) {
        if (converted < 0) capture_error();
        else snprintf(last_error, sizeof(last_error), "Invalid scheduling delay");
        return -5;
    }
    if (JS_IsJobPending(runtime)) return 1;
    if (delay == -1) return 0;
    if (delay > 2147483646.0) delay = 2147483646.0;
    return (int32_t)ceil(delay) + 1;
}

__attribute__((export_name("portable_ui_error")))
int32_t portable_ui_error(char *output, int32_t capacity) {
    if (!output || capacity < 1 || capacity > 512) return -1;
    int32_t length = (int32_t)strlen(last_error);
    if (length >= capacity) length = capacity - 1;
    memcpy(output, last_error, (size_t)length);
    output[length] = 0;
    return length;
}

__attribute__((export_name("portable_ui_destroy")))
void portable_ui_destroy(void) {
    if (context) JS_FreeContext(context);
    if (runtime) JS_FreeRuntime(runtime);
    context = NULL;
    runtime = NULL;
}
