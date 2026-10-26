#include <godot_cpp/classes/ref_counted.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/godot.hpp>
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>

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

class CapsuleRuntime : public RefCounted {
    GDCLASS(CapsuleRuntime, RefCounted)
    void *host = nullptr;
protected:
    static void _bind_methods() {
        ClassDB::bind_method(D_METHOD("open", "library", "config"), &CapsuleRuntime::open);
        ClassDB::bind_method(D_METHOD("exchange", "command", "request"), &CapsuleRuntime::exchange);
        ClassDB::bind_method(D_METHOD("close"), &CapsuleRuntime::close);
    }
public:
    ~CapsuleRuntime() { close(); }
    bool open(String library, String config) {
        close();
        auto filename = library.utf16();
        std::wstring selected(reinterpret_cast<LPCWSTR>(filename.get_data()));
        if (module && selected != module_path) return false;
        if (!module) {
            // Load the coherent native set by full path before NativeAOT P/Invokes.
            // Never depend on the game's working directory or global DLL search path.
            for (const char *name : {"wasmtime.dll", "portable_ui_wasmtime.dll", "portable_ui_yoga.dll"}) {
                auto dependency = library.get_base_dir().path_join(name).utf16();
                if (!LoadLibraryExW(reinterpret_cast<LPCWSTR>(dependency.get_data()), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)) return false;
            }
            module = LoadLibraryExW(reinterpret_cast<LPCWSTR>(library.utf16().get_data()), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
            if (!module) return false;
            module_path = selected;
            auto version = reinterpret_cast<int (__cdecl *)()>(GetProcAddress(module, "capsule_abi_version"));
            if (!version || version() != 1) return false;
            create_host = reinterpret_cast<Create>(GetProcAddress(module, "capsule_create"));
            exchange_host = reinterpret_cast<Exchange>(GetProcAddress(module, "capsule_exchange"));
            free_bytes = reinterpret_cast<Free>(GetProcAddress(module, "capsule_free"));
            destroy_host = reinterpret_cast<Destroy>(GetProcAddress(module, "capsule_destroy"));
        }
        if (!create_host || !exchange_host || !free_bytes || !destroy_host) return false;
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
    if (level == MODULE_INITIALIZATION_LEVEL_SCENE) GDREGISTER_RUNTIME_CLASS(CapsuleRuntime);
}
extern "C" GDExtensionBool GDE_EXPORT capsule_library_init(GDExtensionInterfaceGetProcAddress address, GDExtensionClassLibraryPtr library, GDExtensionInitialization *initialization) {
    GDExtensionBinding::InitObject init(address, library, initialization);
    init.register_initializer(initialize);
    init.set_minimum_library_initialization_level(MODULE_INITIALIZATION_LEVEL_SCENE);
    return init.init();
}
