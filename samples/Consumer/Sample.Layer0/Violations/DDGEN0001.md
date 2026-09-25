---
set: sample-violations
namespace: sample-violations
decisions:
  - key: DuplicatedKey
    statement: "The first of two decisions claiming one key"
  - key: DuplicatedKey
    statement: "The second of them - DDGEN0001"
---

DDGEN0001: two decisions in one ledger namespace claiming the same key. Added to Sample.Layer0's
AdditionalFiles only under DD_SAMPLE_VIOLATIONS, and only there, so it is reported once.
