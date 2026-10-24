#ifndef PUI_BRIDGE_METRICS_H
#define PUI_BRIDGE_METRICS_H
#include <stdint.h>
// Host-owned ABI v1 snapshot. Fixed size, no guest strings or allocations.
// State: 0 idle, 1 pending, 2 completed, 3 trapped, 4 cancelled.
// Operation: 0 unknown, 1 carrier init, 2 SDK init, 3 addon eval,
// 4 update (host may retain trigger flags), 5 viewport, 6 dispose.
typedef struct {
    uint64_t abi_version,generation,invocation,operation,state;
    uint64_t allowance,quantum,consumed,last_slice,polls,yields;
    uint64_t total_fuel,started,completed,trapped,cancelled,rejected;
    uint64_t peak_fuel,execution_ns,last_slice_ns,peak_slice_ns,residence_ns,total_execution_ns;
} pui_invocation_metrics;
#endif
