# samples/Consumer

A consumer, built the way a consumer builds. The analyzers are loaded into every project
here, `docs/decisions/` arrives as `AdditionalFiles`, and the four `Arch*` properties
describe the shape the rules are meant to check.

| Project | `ArchLayer` | `ArchCompositionRoot` | References |
| --- | --- | --- | --- |
| `Sample.Layer0` | 0 | false | — |
| `Sample.Layer1` | 1 | false | `Sample.Layer0` |
| `Sample.Layer2` | 2 | false | `Sample.Layer1` |
| `Sample.Host` | — | **true** | all three layers |
| `Sample.Layer1.Tests` | 1 | false | `Sample.Layer1` |

All five set `ArchFamily=Sample`. `Sample.Host` is the composition root, which is why it
is allowed to name every layer at once; `Sample.Layer1.Tests` is at the layer of the code
it tests.

`Consumer.slnx` gathers all five so that CI compiles every one of them. `Sample.Host`
does not reference `Sample.Layer1.Tests`, so building the host alone would quietly leave
the test sample out of the only thing it is for.

Everything here conforms. That is the point: CI builds these projects and a diagnostic on
any of them fails the build, so the samples are the standing proof that the rules are
silent on code that follows them.

The violating half is not here yet. CI already builds this directory a second time with
`-p:DD_SAMPLE_VIOLATIONS=true`; the define is currently a no-op, and the session that
writes the first rules adds the violating code under it and the expected diagnostics to
the workflow.
