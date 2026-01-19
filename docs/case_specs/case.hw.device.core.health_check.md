# Device Manager Status Check – Spec

## 1. Objective

Check **all device statuses in Device Manager** on the target machine.  
The test **fails** when any device is in a specified abnormal state and is not covered by the Allowlist.

---

## 2. Scope

- Fixed full-device scan  
- Covers all devices enumerated in Device Manager  

---

## 3. Parameters

### 3.1 FailOnStatus
- Type: Multi-select Enum  
- Default: ["Error"]  
- Triggers failure when matched (unless covered by Allowlist)

Supported statuses:
- Error
- Disabled
- Unknown
- NotStarted
- DriverMissing

### 3.2 Allowlist
- Type: JSON Input  
- Default: []

Used to ignore known, acceptable abnormal devices.

### 3.3 AlwaysCollectDeviceList
- Type: Boolean  
- Default: false  

When true, always collect the complete device list regardless of Pass/Fail.

---

## 4. Allowlist Rule Definition

Supported fields (AND relationship within a rule):
- device_name
- hardware_id (recommended)
- class
- status
- note

Multiple rules use OR relationship.

---

## 5. Evaluation Logic

### Pass
- No abnormal devices  
- Or all abnormal devices are covered by Allowlist

### Fail
- At least one abnormal device is not covered by Allowlist

---

## 6. Evidence & Output

### 6.1 Failed Devices (required on Fail)
- device_name
- class
- status
- problem_code
- instance_id
- hardware_ids

### 6.2 All Devices Snapshot (on Fail or AlwaysCollectDeviceList=true)
- device_name
- class
- status
- instance_id
- hardware_ids

---

## 7. Default Behavior

- Full device scan  
- FailOnStatus = ["Error"]  
- Allowlist = []  
- AlwaysCollectDeviceList = false
