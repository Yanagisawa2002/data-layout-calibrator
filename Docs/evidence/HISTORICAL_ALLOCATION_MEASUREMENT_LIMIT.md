# Historical allocation measurement limitation

On 2026-09-07, the integrated Unity 6000.5.3f1 Mono Release Player returned zero
from `GC.GetAllocatedBytesForCurrentThread` for a deliberately retained 4096-byte
array. Its validation stopped before any candidate measurement. The installed
IL2CPP implementation also marks that API as unimplemented. A subsequent Release
probe found the `GC.Alloc` ProfilerRecorder unavailable. Both failed attempts are
retained; neither establishes zero allocation.

Previously retained Unity evidence used the unchecked .NET API. Its recorded zero
allocation fields are unverified observations, not a supported allocation-free
claim. Keep historical JSON, timing samples, decisions and hashes immutable. The
five-process timing hierarchy can still be reproduced byte-for-byte as a
conditional timing/policy estimate, but this replay does not repair the historical
allocation acceptance gate or establish that the old selected policy satisfied
all scientific requirements.

New calibration accepts an injected `IManagedAllocationCounter`, validates real
positive and empty controls outside the timed windows, and records the provider
identity in `ManagedAllocationMeasurement`. The portable .NET default rejects
unsupported implementations. Unity evidence must use a behaviorally validated
runtime provider; missing or unknown observations cannot silently become zero.
The main-thread allocation scope is separate from Burst AOT worker reachability.

This correction changes evidence interpretation rather than rewriting history.
It supplies no new performance, cross-device or compiler-version comparison.
