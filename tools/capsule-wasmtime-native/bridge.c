#include <wasmtime.h>
#include <stdlib.h>
#include <string.h>
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "bridge-metrics.h"
#define API __declspec(dllexport)
typedef int32_t (*host_callback)(void*,int32_t,int64_t,int64_t,int64_t,int64_t,uint8_t*,uint64_t);
typedef struct guest guest;
typedef struct
{
    guest *owner;
    int op;
}
binding;
struct guest
{
    wasmtime_store_t *store;
    wasmtime_context_t *context;
    wasmtime_linker_t *linker;
    wasmtime_instance_t instance;
    wasmtime_memory_t memory;
    host_callback callback;
    void *data;
    binding bindings[15];
    int calls,terminal,host_call_limit;
    uint64_t host_call_rejections;
    uint64_t fuel;
    wasmtime_call_future_t *pending;
    wasmtime_func_t pending_func;
    wasmtime_val_t pending_args[2],pending_result;
    wasm_trap_t *pending_trap;
    wasmtime_error_t *pending_error;
    int pending_results;
    uint64_t pending_started_ns;
    pui_invocation_metrics metrics;
    char error[2048];
}
;
static wasm_engine_t *engine;
static wasmtime_module_t *module;
static uint8_t *module_bytes;
static int module_length;
static SRWLOCK module_lock = SRWLOCK_INIT;
static char bootstrap_error[2048];
static uint64_t timestamp_ns(void)
{
    LARGE_INTEGER counter,frequency;
    QueryPerformanceCounter(&counter);QueryPerformanceFrequency(&frequency);
    return (uint64_t)((double)counter.QuadPart*1000000000.0/(double)frequency.QuadPart);
}
API int pui_cancel(guest *g);
static void message(char *out,wasmtime_error_t *error,wasm_trap_t *trap)
{
    wasm_byte_vec_t text;
    if(error)wasmtime_error_message(error,&text);
    else wasm_trap_message(trap,&text);
    size_t n=text.size<2047?text.size:2047;
    memcpy(out,text.data,n);
    out[n]=0;
    wasm_byte_vec_delete(&text);
    if(error)wasmtime_error_delete(error);
    if(trap)wasm_trap_delete(trap);
}
API const char *pui_bootstrap_error(void)
{
    return bootstrap_error;
}
static int bootstrap(const uint8_t *bytes,int length,const uint8_t *compiled,int compiled_length)
{
    AcquireSRWLockExclusive(&module_lock);
    if(!bytes||length<=0){strcpy(bootstrap_error,"empty carrier");goto failed;}
    if(module&&length==module_length&&!memcmp(bytes,module_bytes,length))
    {ReleaseSRWLockExclusive(&module_lock);return 0;}
    if(!engine){
    wasm_config_t *config=wasm_config_new();
    wasmtime_config_consume_fuel_set(config,true);
    wasmtime_config_max_wasm_stack_set(config,524288);
    wasmtime_config_strategy_set(config,WASMTIME_STRATEGY_CRANELIFT);
    wasmtime_config_cranelift_opt_level_set(config,WASMTIME_OPT_LEVEL_SPEED);
    wasmtime_error_t *target_error=wasmtime_config_target_set(config,"x86_64-pc-windows-msvc");
    if(target_error){message(bootstrap_error,target_error,NULL);wasm_config_delete(config);goto failed;}
    engine=wasm_engine_new_with_config(config);
    }
    wasmtime_module_t *candidate=NULL;
    wasmtime_error_t *error=compiled
        ?wasmtime_module_deserialize(engine,compiled,(size_t)compiled_length,&candidate)
        :wasmtime_module_new(engine,bytes,length,&candidate);
    if(error)
    {
        message(bootstrap_error,error,NULL);
        goto failed;
    }
    uint8_t *copy=malloc(length);
    if(!copy){wasmtime_module_delete(candidate);strcpy(bootstrap_error,"carrier allocation failed");goto failed;}
    memcpy(copy,bytes,length);
    if(module)wasmtime_module_delete(module);
    free(module_bytes);
    module=candidate;module_bytes=copy;module_length=length;
    bootstrap_error[0]=0;
    ReleaseSRWLockExclusive(&module_lock);
    return 0;
    failed:ReleaseSRWLockExclusive(&module_lock);return -1;
}
API int pui_bootstrap(const uint8_t *bytes,int length)
{
    return bootstrap(bytes,length,NULL,0);
}
// Serialized modules are executable host artifacts, never addon-provided data.
API int pui_bootstrap_cached(const uint8_t *bytes,int length,const uint8_t *compiled,int compiled_length)
{
    if(!compiled||compiled_length<=0||compiled_length>16777216)return -1;
    return bootstrap(bytes,length,compiled,compiled_length);
}
API int pui_precompile(const uint8_t *bytes,int length,uint8_t **out,int *out_length)
{
    if(!out||!out_length)return -1;
    *out=NULL;*out_length=0;
    if(pui_bootstrap(bytes,length))return -1;
    AcquireSRWLockExclusive(&module_lock);
    wasm_byte_vec_t serialized;
    wasmtime_error_t *error=wasmtime_module_serialize(module,&serialized);
    if(error){message(bootstrap_error,error,NULL);ReleaseSRWLockExclusive(&module_lock);return -1;}
    if(serialized.size>16777216){wasm_byte_vec_delete(&serialized);ReleaseSRWLockExclusive(&module_lock);return -1;}
    uint8_t *copy=malloc(serialized.size);
    if(!copy){wasm_byte_vec_delete(&serialized);ReleaseSRWLockExclusive(&module_lock);return -1;}
    memcpy(copy,serialized.data,serialized.size);*out=copy;*out_length=(int)serialized.size;
    wasm_byte_vec_delete(&serialized);
    ReleaseSRWLockExclusive(&module_lock);
    return 0;
}
API void pui_precompiled_free(uint8_t *bytes){free(bytes);}
static wasm_trap_t *invoke(void *env,wasmtime_caller_t *caller,const wasmtime_val_t *args,size_t nargs,wasmtime_val_t *results,size_t nresults)
{
    binding *b=env;
    guest *g=b->owner;
    if(++g->calls>g->host_call_limit)
    {
        g->host_call_rejections++;
        return wasmtime_trap_new("host call quota",15);
    }
    wasmtime_extern_t ext;
    if(!wasmtime_caller_export_get(caller,"memory",6,&ext))return wasmtime_trap_new("missing memory",14);
    wasmtime_context_t *ctx=wasmtime_caller_context(caller);
    int64_t a[4]=
    {
        0
    }
    ;
    for(size_t i=0;i<nargs;i++)a[i]=args[i].kind==WASMTIME_I64?args[i].of.i64:args[i].of.i32;
    int result=g->callback(g->data,b->op,a[0],a[1],a[2],a[3],wasmtime_memory_data(ctx,&ext.of.memory),wasmtime_memory_data_size(ctx,&ext.of.memory));
    if(b->op==14)return wasmtime_trap_new("process exit denied",19);
    if(nresults)
    {
        results[0].kind=WASMTIME_I32;
        results[0].of.i32=result;
    }
    return NULL;
}
static int bind(guest *g,int op,const char *ns,const char *name,const char *params,int returns)
{
    wasm_valtype_vec_t p,r;
    size_t n=strlen(params);
    wasm_valtype_vec_new_uninitialized(&p,n);
    for(size_t i=0;i<n;i++)p.data[i]=wasm_valtype_new(params[i]=='l'?WASM_I64:WASM_I32);
    wasm_valtype_vec_new_uninitialized(&r,returns);
    if(returns)r.data[0]=wasm_valtype_new(WASM_I32);
    wasm_functype_t *type=wasm_functype_new(&p,&r);
    g->bindings[op]=(binding)
    {
        g,op
    }
    ;
    wasmtime_error_t *error=wasmtime_linker_define_func(g->linker,ns,strlen(ns),name,strlen(name),type,invoke,&g->bindings[op],NULL);
    wasm_functype_delete(type);
    if(error)
    {
        message(g->error,error,NULL);
        return -1;
    }
    return 0;
}
API guest *pui_create_with_host_call_limit(host_callback cb,void *data,int limit)
{
    if(limit!=64&&limit!=2048)
    {
        strcpy(bootstrap_error,"invalid host call limit");
        return NULL;
    }
    AcquireSRWLockShared(&module_lock);
    if(!module){ReleaseSRWLockShared(&module_lock);return NULL;}
    guest *g=calloc(1,sizeof(guest));
    if(!g){ReleaseSRWLockShared(&module_lock);return NULL;}
    g->host_call_limit=limit;
    g->metrics.abi_version=1;
    g->callback=cb;
    g->data=data;
    g->store=wasmtime_store_new(engine,NULL,NULL);
    g->context=wasmtime_store_context(g->store);
    wasmtime_store_limiter(g->store,8388608,4096,1,1,1);
    g->linker=wasmtime_linker_new(engine);
    const char *ns="portable_ui_v1";
    #define B(op,name,params,ret) if(bind(g,op,ns,name,params,ret))goto fail
    B(0,"ui_reserve_handle","i",1);
    B(1,"ui_submit","ii",1);
    B(2,"ui_next_event","ii",1);
    B(3,"game_next_observation","ii",1);
    B(4,"game_request_action","liii",1);
    B(5,"package_read_data","iiii",1);
    B(6,"package_acquire_image","iiii",1);
    ns="wasi_snapshot_preview1";
    B(7,"args_get","ii",1);
    B(8,"args_sizes_get","ii",1);
    B(9,"clock_time_get","ili",1);
    B(10,"fd_fdstat_get","ii",1);
    B(11,"fd_seek","ilii",1);
    B(12,"fd_write","iiii",1);
    B(13,"fd_close","i",1);
    B(14,"proc_exit","i",0);
    #undef B
    wasmtime_context_set_fuel(g->context,200000);
    wasm_trap_t *trap=NULL;
    wasmtime_error_t *error=wasmtime_linker_instantiate(g->linker,g->context,module,&g->instance,&trap);
    if(error||trap)
    {
        message(g->error,error,trap);
        goto fail;
    }
    wasmtime_extern_t ext;
    if(!wasmtime_instance_export_get(g->context,&g->instance,"memory",6,&ext))
    {
        strcpy(g->error,"memory missing");
        goto fail;
    }
    g->memory=ext.of.memory;
    ReleaseSRWLockShared(&module_lock);
    return g;
    fail:strcpy(bootstrap_error,g->error);
    wasmtime_linker_delete(g->linker);
    wasmtime_store_delete(g->store);
    free(g);
    ReleaseSRWLockShared(&module_lock);
    return NULL;
}
API guest *pui_create(host_callback cb,void *data)
{
    return pui_create_with_host_call_limit(cb,data,64);
}
API uint64_t pui_host_call_rejections(guest *g)
{
    return g?g->host_call_rejections:0;
}
API int pui_call(guest *g,const char *name,int a,int b,int nargs,int nresults,uint64_t fuel,int *result)
{
    if(!g||g->terminal)return -1;
    if(g->pending||g->metrics.started){g->metrics.rejected++;return -2;}
    g->calls=0;
    wasmtime_error_t *error=wasmtime_context_set_fuel(g->context,fuel);
    if(error)
    {
        message(g->error,error,NULL);
        g->terminal=1;
        return -1;
    }
    wasmtime_extern_t ext;
    if(!wasmtime_instance_export_get(g->context,&g->instance,name,strlen(name),&ext))
    {
        strcpy(g->error,"export missing");
        g->terminal=1;
        return -1;
    }
    wasmtime_val_t args[2]=
    {
        {
            .kind=WASMTIME_I32,.of.i32=a
        }
        ,
        {
            .kind=WASMTIME_I32,.of.i32=b
        }
    }
    ,out;
    wasm_trap_t *trap=NULL;
    error=wasmtime_func_call(g->context,&ext.of.func,args,nargs,&out,nresults,&trap);
    uint64_t remaining=0;
    wasmtime_context_get_fuel(g->context,&remaining);
    g->fuel=fuel-remaining;
    if(error||trap)
    {
        message(g->error,error,trap);
        g->terminal=1;
        return -1;
    }
    if(nresults)*result=out.of.i32;
    return 0;
}
static void pending_account(guest *g)
{
    uint64_t remaining=0;
    wasmtime_error_t *error=wasmtime_context_get_fuel(g->context,&remaining);
    if(error){message(g->error,error,NULL);g->terminal=1;return;}
    uint64_t consumed=g->metrics.allowance-remaining;
    g->metrics.last_slice=consumed-g->metrics.consumed;
    g->metrics.total_fuel+=g->metrics.last_slice;
    g->metrics.consumed=consumed;
    g->fuel=consumed;
}
static void pending_finish(guest *g,uint64_t state)
{
    g->metrics.state=state;
    g->metrics.residence_ns=timestamp_ns()-g->pending_started_ns;
    if(g->metrics.consumed>g->metrics.peak_fuel)g->metrics.peak_fuel=g->metrics.consumed;
    if(state==2)g->metrics.completed++;
    else if(state==3)g->metrics.trapped++;
    else if(state==4)g->metrics.cancelled++;
}
// Experimental asynchronous ABI. Parameters/results live in the guest until
// future deletion. Caller serializes begin/poll/cancel on one owning worker.
API int pui_begin(guest *g,const char *name,int a,int b,int nargs,int nresults,
    uint64_t allowance,uint64_t quantum,uint64_t generation,uint64_t operation)
{
    if(!g||g->terminal)return -1;
    if(g->pending){g->metrics.rejected++;return -2;}
    if(!name||nargs<0||nargs>2||nresults<0||nresults>1||!allowance||!quantum||!generation||
        (g->metrics.started&&generation!=g->metrics.generation)){g->metrics.rejected++;return -2;}
    g->metrics.abi_version=1;g->metrics.generation=generation;g->metrics.invocation++;
    g->metrics.operation=operation;g->metrics.state=1;g->metrics.allowance=allowance;g->metrics.quantum=quantum;
    g->metrics.consumed=g->metrics.last_slice=g->metrics.polls=g->metrics.yields=0;
    g->metrics.execution_ns=g->metrics.last_slice_ns=g->metrics.residence_ns=0;
    g->metrics.started++;g->pending_started_ns=timestamp_ns();g->calls=0;g->fuel=0;
    g->pending_error=wasmtime_context_set_fuel(g->context,allowance);
    if(!g->pending_error)g->pending_error=wasmtime_context_fuel_async_yield_interval(g->context,quantum);
    wasmtime_extern_t ext;
    if(g->pending_error||!wasmtime_instance_export_get(g->context,&g->instance,name,strlen(name),&ext)||ext.kind!=WASMTIME_EXTERN_FUNC)
    {
        if(g->pending_error){message(g->error,g->pending_error,NULL);g->pending_error=NULL;}
        else strcpy(g->error,"export missing or not a function");
        g->terminal=1;pending_finish(g,3);return -1;
    }
    g->pending_func=ext.of.func;
    g->pending_args[0]=(wasmtime_val_t){.kind=WASMTIME_I32,.of.i32=a};
    g->pending_args[1]=(wasmtime_val_t){.kind=WASMTIME_I32,.of.i32=b};
    g->pending_result=(wasmtime_val_t){0};g->pending_trap=NULL;g->pending_error=NULL;g->pending_results=nresults;
    g->pending=wasmtime_func_call_async(g->context,&g->pending_func,g->pending_args,nargs,
        &g->pending_result,nresults,&g->pending_trap,&g->pending_error);
    return 0;
}
// 1 yielded, 0 completed, -1 trapped/terminal, -2 invalid lifecycle request.
API int pui_poll(guest *g,int *result)
{
    if(!g||g->terminal)return -1;
    if(!g->pending){g->metrics.rejected++;return -2;}
    uint64_t begin=timestamp_ns();
    int completed=wasmtime_call_future_poll(g->pending);
    uint64_t elapsed=timestamp_ns()-begin;
    g->metrics.last_slice_ns=elapsed;g->metrics.execution_ns+=elapsed;g->metrics.total_execution_ns+=elapsed;
    if(elapsed>g->metrics.peak_slice_ns)g->metrics.peak_slice_ns=elapsed;
    g->metrics.polls++;pending_account(g);
    g->metrics.residence_ns=timestamp_ns()-g->pending_started_ns;
    if(!completed){g->metrics.yields++;return 1;}
    wasmtime_call_future_delete(g->pending);g->pending=NULL;
    if(g->pending_error||g->pending_trap)
    {
        message(g->error,g->pending_error,g->pending_trap);g->pending_error=NULL;g->pending_trap=NULL;
        g->terminal=1;pending_finish(g,3);return -1;
    }
    if(g->pending_results&&result)*result=g->pending_result.of.i32;
    pending_finish(g,2);return 0;
}
API int pui_cancel(guest *g)
{
    if(!g||!g->pending)return -2;
    // No concurrent poll is permitted. Dropping a parked future terminates this
    // guest; partially mutated QuickJS state is never invoked again.
    wasmtime_call_future_delete(g->pending);g->pending=NULL;pending_account(g);
    if(g->pending_error)wasmtime_error_delete(g->pending_error);
    if(g->pending_trap)wasm_trap_delete(g->pending_trap);
    g->pending_error=NULL;g->pending_trap=NULL;g->terminal=1;pending_finish(g,4);return 0;
}
API int pui_metrics(guest *g,pui_invocation_metrics *out)
{
    if(!g||!out)return -2;*out=g->metrics;return 0;
}
API uint8_t *pui_memory(guest *g,uint64_t *size)
{
    if(!g||g->pending){if(size)*size=0;return NULL;}
    *size=wasmtime_memory_data_size(g->context,&g->memory);
    return wasmtime_memory_data(g->context,&g->memory);
}
API uint64_t pui_fuel(guest *g)
{
    return g->fuel;
}
API const char *pui_error(guest *g)
{
    return g->error;
}
API void pui_destroy(guest *g)
{
    if(!g)return;
    if(g->pending)pui_cancel(g);
    wasmtime_store_delete(g->store);
    wasmtime_linker_delete(g->linker);
    free(g);
}
API void pui_destroy_measured(guest *g,pui_invocation_metrics *out)
{
    if(!g)return;if(g->pending)pui_cancel(g);if(out)*out=g->metrics;pui_destroy(g);
}

