# Test Protocol Reporter

The Test Protocol Reporter package (`com.unity.test-framework.utp-reporter`) reports the results of
a Unity Test Framework run from inside a **player** as Unity Test Protocol (UTP) messages.

The Editor already emits UTP for the runs it hosts. A player does not: it runs the tests itself and
has no channel to the process that started it beyond its log and its player connection. This package
is that channel. It subscribes to the test run in the player, converts what the run reports into
the equivalent UTP messages, and emits them over the transport the run was started with — so a harness such
as the Unified Test Runner sees the same protocol from a player run as from an Editor run.

## Requirements

* Unity 6000.3 or later.
* `com.unity.test-framework` — the run this package reports on.
* `com.unity.external.test-protocol` — the UTP message types and their serialization.

## Installation

The package is a dependency of the test tooling that runs the tests, and is installed for you.
Adding it to a project by hand is not expected: it reports to whoever started the run, and in the
default `player-connection` mode the player announces that the run has finished and then waits to
be quit by the receiver that consumed the messages. With no such receiver the player never exits
on its own.

## How the reporter is installed into a run

`TestResultToUtpMessage` implements `ITestRunCallback` and is registered with an assembly-level
attribute that is compiled in only outside the Editor:

```csharp
#if !UNITY_EDITOR
[assembly: Preserve]
[assembly: TestRunCallback(typeof(TestResultToUtpMessage))]
#endif
```

So the reporter attaches itself to every player test run, with no setup in the project under test,
and never double-reports alongside the Editor's own producer. `[assembly: Preserve]` keeps the
callback from being stripped out of a managed-code-stripped player.

The reporter is only useful in a build that contains tests, so the Editor assembly contributes
`TestBuildAssemblyFilter`, an `IFilterBuildAssemblies` that drops `Unity.TestFramework.UTPReporter`
from any build made without `BuildOptions.IncludeTestAssemblies`.

## Output modes

The player chooses a transport from its own command line:

```
-utp-output-mode player-connection   (default)
-utp-output-mode log
```

The argument is matched case-insensitively. If it is absent, or its value is neither of the two
above, the reporter uses `player-connection`.

### `player-connection`

Each message is sent over the player connection as UTF-8 on message id
`28e419da-b96b-4e57-8a27-17330f0e0b6f`.

When the run ends, the reporter sends a single byte on message id
`8eb67a7f-8faf-4990-8e3d-9a3ea8fab600` and then **does not quit**. Player-connection payloads can
still be in flight at that point, so shutting down there would truncate the run. The receiving side
acknowledges the end of the run and asks the player to quit once it has consumed everything.

### `log`

Each message is written to the player log through `Debug.LogFormat` with `LogOption.NoStacktrace`,
prefixed with `##utp:` so a log reader can pick the protocol back out of ordinary log output. The
prefix is added only if the serialized message does not already carry it.

There is no completion handshake in this mode, so the reporter calls `Application.Quit()` once it has
emitted the final messages and relies on Unity's orderly shutdown to flush the log.

## What is reported

The reporter is an `ITestRunCallback`, so it reads the NUnit nodes the test framework hands over
rather than a serialized copy of them. Each callback is converted to UTP:

| Callback | UTP message |
|---|---|
| `RunStarted` / `RunFinished` | `TestGroup` start and end for the run's root suite |
| `TestStarted` on a suite | `TestGroup` start, named with the suite's full name — an assembly suite by its file rather than its full on-disk path |
| `TestFinished` on a suite | `TestGroup` end |
| `TestStarted` on a test | `TestStatus` Begin |
| `TestFinished` on a test | `TestStatus` End, preceded by a synthesized Begin if none was reported |

The root suite arrives twice, once through the run callbacks and again as an ordinary test callback,
so the run callbacks own that pair and the test callbacks skip it.

A node's parent is read from `ITest.Parent`, so the shape of the subtree never depends on the order
events arrive in. Only the outermost group leaves `parentSpanId` absent — absent rather than
`Guid.Empty`, which a consumer honours as "I am a root" and would use to detach the subtree from its
own scope.

Both the Begin and the End of a test carry the identity a consumer needs to match them up:

* **`uniqueName`**, the key a consumer matches a test on, derived the way the Editor derives it so
  that a player computes the same key for the same test.
* **`spanId`** and **`parentSpanId`**, derived from `uniqueName` and the attempt index, so a Begin
  and its End resolve to the same span without either being remembered.
* **`classname`** and **`methodname`**. An attempt that never reports an End — the player died
  while the test was running — is only ever seen through its Begin, so identity the Begin omits
  cannot be recovered.
* **`categories`**, merging the categories the test declares itself with those declared above it. A
  category can be set on the fixture or on an enclosing suite, so the whole ancestor chain is
  unioned.
* **`iteration`**, the attempt index, reported only when it is non-zero. A repeat iteration takes
  precedence over a retry iteration, matching the precedence the Editor's reporter applies.

The End additionally carries the outcome, the duration in microseconds, the failure message and the
stack trace.

## Document revision history

| Date | Reason |
|---|---|
| 2026-09-21 | Updated what is reported for the `ITest`-sourced converter. |
| 2026-09-18 | Replaced the package documentation template with documentation of the package. |
