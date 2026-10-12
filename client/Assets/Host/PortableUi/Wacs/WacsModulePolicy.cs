using System;
using System.IO;
using Wacs.Core;

namespace PortableUi.Wacs
{
    // Conservative first-spike module subset; check before WACS instantiates or allocates memory.
    public static class WacsModulePolicy
    {
        public const int MaxModuleBytes = 1024 * 1024;
        public const int MaxMemoryPages = 128;
        public const int MaxTableElements = 1024;

        public static bool TryParse(TrustedWasmCarrier carrier, out Module module, out string failure,
            bool quickJsCarrier = false)
        {
            module = null;
            failure = null;
            if (carrier == null)
            { failure = "Host-approved Wasm carrier is required."; return false; }
            try
            {
                using (var stream = new MemoryStream(carrier.CopyApprovedBytes(), false))
                    module = BinaryModuleParser.ParseWasm(stream);
                if (module.StartIndex != global::Wacs.Core.Types.FuncIdx.Default)
                { failure = "Start function is forbidden in this prototype."; return false; }
                if (module.ImportedMems.Count != 0 || module.ImportedTables.Count != 0 ||
                    module.ImportedGlobals.Count != 0 || module.ImportedTags.Count != 0)
                { failure = "Imported memory, tables, globals and tags are forbidden."; return false; }
                if (module.MemoryCount != 1 || module.Memories.Count != 1)
                { failure = "Exactly one defined memory is required."; return false; }
                var limit = module.Memories[0].Limits;
                if (limit.AddressType != global::Wacs.Core.Types.Defs.AddrType.I32 || limit.Shared ||
                    !limit.Maximum.HasValue || limit.Minimum < 1 ||
                    limit.Maximum.Value > MaxMemoryPages || limit.Minimum > limit.Maximum.Value)
                { failure = "Memory must be wasm32, unshared and declare 1..128 maximum pages."; return false; }
                if (module.Tables.Count > 1 || module.Globals.Count > 128 ||
                    module.Elements.Length > 128 || module.DataCount > 128)
                { failure = "Table, global or segment quota exceeded."; return false; }
                foreach (var table in module.Tables)
                    if (!table.Limits.Maximum.HasValue ||
                        table.Limits.Maximum.Value > MaxTableElements ||
                        table.Limits.Minimum > table.Limits.Maximum.Value)
                    { failure = "Table must declare at most 1024 elements."; return false; }
                foreach (var import in module.Imports)
                {
                    if (!(import.Desc is Module.ImportDesc.FuncDesc) ||
                        !(import.ModuleName == "portable_ui_v1" && AllowedImport(import.Name) ||
                          quickJsCarrier && import.ModuleName == "wasi_snapshot_preview1" &&
                          AllowedQuickJsWasi(import.Name)))
                    { failure = "Unsupported import: " + import.ModuleName + "." + import.Name; return false; }
                }
                return true;
            }
            catch (Exception error)
            {
                failure = "Wasm parse/validation failed: " + error.Message;
                module = null;
                return false;
            }
        }

        static bool AllowedImport(string name) => name == "ui_reserve_handle" ||
            name == "ui_submit" || name == "ui_next_event" ||
            name == "game_next_observation" || name == "game_request_action" ||
            name == "package_read_data" || name == "package_acquire_image";

        // Fixed libc imports of the host-pinned QuickJS carrier. No filesystem,
        // process, network, environment or descriptor capability is granted.
        static bool AllowedQuickJsWasi(string name) =>
            name == "args_get" || name == "args_sizes_get" ||
            name == "clock_time_get" || name == "fd_fdstat_get" ||
            name == "fd_seek" || name == "fd_write" ||
            name == "proc_exit" || name == "fd_close";
    }
}
