# The Frontend Is React, Not Blazor

logaffe is a .NET product written for operators of .NET services, so Blazor is
the frontend a reader expects and its absence needs a reason. Blazor Server is
ruled out by the product itself: it holds a stateful circuit over a persistent
WebSocket, which is the connection-lifecycle, proxy and reconnect problem
`VISION.md` already refused when it chose polling over SSE and WebSockets for
following logs live — refusing push for the log view and then taking a permanent
socket for the whole UI would be incoherent. That leaves Blazor WebAssembly,
which does satisfy the API-first shape, against React; the choice went to React
because the one screen that matters is a virtualized, text-dense list refreshed
every few seconds, and the components that do that well are a solved problem in
React and a thin field in Blazor WASM, whose runtime download is additionally
paid on every cold load of a UI whose whole job is to be opened quickly when
something is broken.

## Consequences

The repository carries two languages and two toolchains, and the frontend cannot
share C# types with the backend — the HTTP contract has to be written down and
kept honest by tests rather than by the compiler. That cost is smaller here than
it looks, because `VISION.md` fixes the web UI as a client of the same documented
API that MCP and the ingestion path use, so an explicit contract was owed
anyway. The open-source intent points the same way: a contributor arriving at a
self-hosted logging tool is far likelier to bring React than Blazor.
