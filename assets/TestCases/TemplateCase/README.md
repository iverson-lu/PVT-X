# TemplateCase

A template test case for PVT-X that demonstrates a **standardized** approach to:

- Logging to **stdout/stderr** with consistent prefixes
- Writing test-owned outputs under `artifacts/`
- Using stable **exit codes**:
  - `0` = Pass
  - `1` = Fail (assertion/check failed)
  - `2` = Error (script/runtime/dependency error)

> Runner/Engine is responsible for generating `result.json`, `env.json`, `manifest.json`, capturing stdout/stderr, etc.
> This test only writes to `artifacts/`.

---

## Files

