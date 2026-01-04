# PVT-X Test Case ID Schema (4-Level Design)

## 1. Purpose

This document defines the **official 4-level identifier schema** for PVT-X test cases.  
The schema is designed for **long-term scalability**, **cross-team consistency**, and **machine-friendly governance**, while remaining readable for humans.

```
case.<domain>.<subsystem>.<feature>.<action>
```

This schema is **mandatory** for all production test cases.

---

## 2. Design Rationale

A 3-level schema (`domain.feature.action`) becomes ambiguous as the test library grows:

- `feature` is forced to represent both *subsystem* and *capability*
- Naming drifts over time (e.g. `power_sleep`, `sleep_power`, `power_state`)
- UI grouping and filtering become unstable

The 4-level schema explicitly separates concerns:

| Level | Responsibility |
|------|----------------|
| `domain` | High-level technical area |
| `subsystem` | Major functional block |
| `feature` | Specific mechanism / capability |
| `action` | Test intent |

This structure enables:
- Stable naming conventions
- Strong whitelist validation
- Predictable UI grouping
- Future automation and analytics

---

## 3. Schema Definition

### 3.1 `domain`

Represents the top-level technical domain.

Examples:
```
hw
os
sw
fw
bios
network
security
platform
perf
stress
diagnostic
```

---

### 3.2 `subsystem`

Represents a **major functional subsystem** within a domain.

Examples:
```
power
storage
wifi
ethernet
boot
update
memory
cpu
```

Rules:
- Must be selected from an official whitelist
- Must be meaningful without additional context

---

### 3.3 `feature`

Represents a **specific capability, mechanism, or focus area** inside a subsystem.

Examples:
```
s3
s4
battery
charger
nvme
sata
smart
scan
connect
roam
```

#### Special Value: `core`

If a subsystem does not require further subdivision, `feature` **MUST** be set to `core`.

Examples:
```
power.core
boot.core
update.core
```

This guarantees a **uniform 4-level structure** across all cases.

---

### 3.4 `action`

Represents the **test intent or behavior**.

Recommended verbs:
```
check
verify
detect
validate
test
stress
cycle
monitor
measure
compare
recover
restore
```

Lifecycle / state actions:
```
boot
shutdown
sleep
resume
restart
install
uninstall
update
rollback
enable
disable
```

Rules:
- Lowercase only
- `_` allowed
- Maximum length: 32 characters

---

## 4. Complete Examples

### Hardware
```
case.hw.power.core.sleep_resume
case.hw.power.s3.cycle
case.hw.storage.nvme.smart_check
case.hw.cpu.core.stress_test
```

### Operating System
```
case.os.boot.core.time_check
case.os.update.core.rollback_check
case.os.sleep.core.resume_verify
```

### Network
```
case.network.wifi.connect.basic_check
case.network.wifi.roam.stability_test
case.network.ethernet.core.link_detect
```

### Security
```
case.security.secureboot.core.enable_check
case.security.user.permission.verify
```

---

## 5. Mandatory Rules

- All test case IDs MUST follow the 4-level schema
- `domain`, `subsystem`, and `feature` MUST come from official whitelists
- `action` MUST be descriptive and human-readable
- Version information MUST NOT appear in the ID
- Versioning MUST use the `version` field in the manifest

---

## 6. Design Principle

> **If the structure of an ID is inconsistent, the system cannot scale.**  
> **If the meaning of an ID is unclear, the ID is invalid.**

---

End of document.

