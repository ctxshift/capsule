#include <godot_cpp/classes/ref_counted.hpp>
#include <godot_cpp/classes/engine.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/godot.hpp>
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <chrono>

using namespace godot;
using Create = void *(__cdecl *)(const char *, int);
using Exchange = char *(__cdecl *)(void *, int, const char *, int);
using Free = void (__cdecl *)(void *);
using Destroy = void (__cdecl *)(void *);
// NativeAOT modules are process-lifetime libraries and must never be unloaded.
static HMODULE module = nullptr;
static std::wstring module_path;
static Create create_host;
static Exchange exchange_host;
static Free free_bytes;
static Destroy destroy_host;
static double preload_milliseconds = 0;

static bool load_native(const std::wstring &path) {
    wchar_t filename[32768];
    DWORD length = GetFullPathNameW(path.c_str(), 32768, filename, nullptr);
    if (!length || length >= 32768) return false;
    std::wstring selected(filename, length);
    for (auto &character : selected) if (character == L'/') character = L'\\';
    if (module && CompareStringOrdinal(selected.c_str(), -1, module_path.c_str(), -1, TRUE) != CSTR_EQUAL) return false;
    if (!module) {
        auto directory = selected.substr(0, selected.find_last_of(L"\\/") + 1);
        for (const wchar_t *name : {L"wasmtime.dll", L"portable_ui_wasmtime.dll", L"portable_ui_yoga.dll"}) {
            auto dependency = directory + name;
            if (!LoadLibraryExW(dependency.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)) return false;
        }
        module = LoadLibraryExW(selected.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!module) return false;
        module_path = selected;
        auto version = reinterpret_cast<int (__cdecl *)()>(GetProcAddress(module, "capsule_abi_version"));
        if (!version || version() != 1) return false;
        create_host = reinterpret_cast<Create>(GetProcAddress(module, "capsule_create"));
        exchange_host = reinterpret_cast<Exchange>(GetProcAddress(module, "capsule_exchange"));
        free_bytes = reinterpret_cast<Free>(GetProcAddress(module, "capsule_free"));
        destroy_host = reinterpret_cast<Destroy>(GetProcAddress(module, "capsule_destroy"));
    }
    return create_host && exchange_host && free_bytes && destroy_host;
}

static void preload_native() {
    auto started = std::chrono::steady_clock::now();
    HMODULE self = nullptr;
    wchar_t filename[32768];
    if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&module), &self)) {
        DWORD length = GetModuleFileNameW(self, filename, 32768);
        if (length && length < 32768) {
            std::wstring selected(filename, length);
            selected = selected.substr(0, selected.find_last_of(L"\\/") + 1) + L"capsule_host.dll";
            if (load_native(selected)) {
                auto prepare = reinterpret_cast<int (__cdecl *)(const char *, int)>(GetProcAddress(module, "capsule_prepare"));
                if (prepare) {
                    auto directory = selected.substr(0, selected.find_last_of(L"\\/"));
                    int count = WideCharToMultiByte(CP_UTF8, 0, directory.c_str(), static_cast<int>(directory.size()), nullptr, 0, nullptr, nullptr);
                    std::string encoded(count, '\0');
                    if (count > 0 && WideCharToMultiByte(CP_UTF8, 0, directory.c_str(), static_cast<int>(directory.size()), encoded.data(), count, nullptr, nullptr))
                        prepare(encoded.data(), count);
                }
            }
        }
    }
    preload_milliseconds = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - started).count();
}

class CapsuleRuntime : public RefCounted {
    GDCLASS(CapsuleRuntime, RefCounted)
    void *host = nullptr;
protected:
    static void _bind_methods() {
        ClassDB::bind_method(D_METHOD("open", "library", "config"), &CapsuleRuntime::open);
        ClassDB::bind_method(D_METHOD("exchange", "command", "request"), &CapsuleRuntime::exchange);
        ClassDB::bind_method(D_METHOD("close"), &CapsuleRuntime::close);
        ClassDB::bind_method(D_METHOD("get_preload_milliseconds"), &CapsuleRuntime::get_preload_milliseconds);
    }
public:
    ~CapsuleRuntime() { close(); }
    double get_preload_milliseconds() const { return preload_milliseconds; }
    bool open(String library, String config) {
        close();
        auto filename = library.utf16();
        std::wstring selected(reinterpret_cast<LPCWSTR>(filename.get_data()));
        if (!load_native(selected)) return false;
        auto text = config.utf8();
        host = create_host(text.get_data(), text.length());
        return host != nullptr;
    }
    String exchange(int command, String request) {
        if (!exchange_host) return "{\"error\":\"Capsule native library could not load.\"}";
        auto text = request.utf8();
        char *response = exchange_host(host, command, text.get_data(), text.length());
        if (!response) return "{\"error\":\"Capsule native response missing.\"}";
        String result = String::utf8(response);
        free_bytes(response);
        return result;
    }
    void close() { if (host && destroy_host) destroy_host(host); host = nullptr; }
};

static void initialize(ModuleInitializationLevel level) {
    if (level == MODULE_INITIALIZATION_LEVEL_SCENE) {
        if (!Engine::get_singleton()->is_editor_hint()) preload_native();
        GDREGISTER_RUNTIME_CLASS(CapsuleRuntime);
    }
}
extern "C" GDExtensionBool GDE_EXPORT capsule_library_init(GDExtensionInterfaceGetProcAddress address, GDExtensionClassLibraryPtr library, GDExtensionInitialization *initialization) {
    GDExtensionBinding::InitObject init(address, library, initialization);
    init.register_initializer(initialize);
    init.set_minimum_library_initialization_level(MODULE_INITIALIZATION_LEVEL_SCENE);
    return init.init();
}
