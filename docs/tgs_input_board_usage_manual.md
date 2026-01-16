# TGS Input Board - Usage Manual

Firmware name: TGS Input Board  
Firmware version: 1.2  
Protocol: JSON over Serial (newline-delimited)  
Config persistence: LittleFS (`/tgs_input_config.txt`)  
Inputs supported: Up to 32 configured (hardware may expose fewer)

---

## 1. Overview

The TGS Input Board is a USB-connected input controller designed to read multiple digital inputs (buttons, switches, sensors) and communicate state changes to a host computer.

Each configured input can:

- Emit asynchronous JSON events over Serial
- Optionally act as a USB Keyboard key (press/release)

Configuration is persistent and stored in the board internal filesystem.

---

## 2. Electrical Characteristics

### 2.1 Input Mode

- All inputs use `INPUT_PULLUP`
- Default idle state: `HIGH`
- Active state: `LOW`

### 2.2 Wiring

Typical button wiring:

- One terminal connected to the input pin
- Other terminal connected to GND

Electrical state mapping:

| Electrical level | Reported state |
| ---------------- | -------------- |
| HIGH             | open           |
| LOW              | closed         |

IMPORTANT:  
The `invert` option affects **keyboard logic only**.  
It does **NOT** affect the reported `open/closed` event state.

---

## 3. USB and Serial Interface

- USB interfaces:
  - CDC Serial
  - Optional HID Keyboard
- Default baud rate: `9600`
- Line-based JSON protocol terminated by newline (`\n`)

---

## 4. Message Types

All messages are JSON objects sent/received over Serial.

### 4.1 Responses

Responses always use:

- `type = "resp"`
- `ok = true/false`
- `cmd = "<command>"`

Example OK:

```json
{"type":"resp","ok":true,"cmd":"export"}
```

Example error:

```json
{"type":"resp","ok":false,"cmd":"import","error":"args"}
```

### 4.2 Events

Events always use:

- `type = "event"`
- `pin = <gpio>`
- `state = "open" | "closed"`

Examples:

```json
{"type":"event","pin":12,"state":"closed"}
```

```json
{"type":"event","pin":12,"state":"open"}
```

State meanings:

- `closed`: pin electrically `LOW`
- `open`: pin electrically `HIGH`

---

## 5. Supported Commands

All commands are JSON objects sent over Serial. Each command expects a response message.

### 5.1 board

Request:

```json
{"cmd":"board"}
```

Response:

```json
{"type":"resp","ok":true,"cmd":"board","board":"TGS Input Board"}
```

### 5.2 version

Request:

```json
{"cmd":"version"}
```

Response:

```json
{"type":"resp","ok":true,"cmd":"version","version":"1.2"}
```

### 5.3 export

Returns the full current configuration snapshot.

Request:

```json
{"cmd":"export"}
```

Response (example):

```json
{
  "type": "resp",
  "ok": true,
  "cmd": "export",
  "data": {
    "schema": 1,
    "cfg_ver": 1,
    "baud": 9600,
    "events": false,
    "keyboard": true,
    "buttons": [
      { "pin": 12, "mode": "tap",  "invert": false, "key": "enter", "debounce": 8,  "tap": 40 },
      { "pin": 13, "mode": "hold", "invert": true,  "key": "space", "debounce": 10, "tap": 40 }
    ]
  }
}
```

Notes:

- `schema` identifies the JSON export/import format.
- `cfg_ver` identifies the persisted config file format/version used by the firmware.

### 5.4 import

Replaces the full configuration in one operation (baud/events/keyboard/buttons).

Request (example):

```json
{
  "cmd": "import",
  "data": {
    "schema": 1,
    "baud": 9600,
    "events": true,
    "keyboard": true,
    "buttons": [
      { "pin": 12, "mode": "tap",  "invert": false, "key": "1",     "debounce": 8, "tap": 40 },
      { "pin": 13, "mode": "hold", "invert": false, "key": "enter", "debounce": 8, "tap": 40 }
    ]
  }
}
```

Response:

```json
{"type":"resp","ok":true,"cmd":"import"}
```

If `baud` changes, the board restarts Serial after acknowledging the command.

### 5.5 reset

Resets configuration to defaults and rewrites the config file.

Request:

```json
{"cmd":"reset"}
```

Response:

```json
{"type":"resp","ok":true,"cmd":"reset"}
```

---

## 6. Input Modes

### 6.1 Tap Mode

- Key is pressed when the input becomes active (logical ON)
- Held for the configured `tap` duration (ms)
- Automatically released

### 6.2 Hold Mode

- Key is pressed while the input is active (logical ON)
- Released when input becomes inactive (logical OFF)

---

## 7. Keyboard Key Mapping

The `key` field is a string.

### 7.1 Single Characters

Examples:

```
a
b
1
9
+
-
```

### 7.2 Special Keys (case-insensitive)

```
enter
tab
esc
space
backspace
del
up
down
left
right
f1 ... f12
```

### 7.3 Numeric scan codes

You can specify numeric codes using:

- `"code:<number>"` (recommended)
- `"<number>"` (digits-only string)

Examples:

```
code:176
176
```

---

## 8. Typical Setup Procedure

1. Connect the board via USB
2. Open a Serial terminal at `9600` baud
3. Identify the board

```json
{"cmd":"board"}
```

4. Read current configuration

```json
{"cmd":"export"}
```

5. Apply full configuration (including events + keyboard + buttons)

```json
{"cmd":"import","data":{"schema":1,"baud":9600,"events":true,"keyboard":true,"buttons":[...]}}
```

6. Verify applied configuration

```json
{"cmd":"export"}
```

7. Listen for events (if enabled)

Events arrive asynchronously, interleaved with responses:

```json
{"type":"event","pin":12,"state":"closed"}
```

---

## 9. Troubleshooting

### No events received

- Ensure `events` is `true` in your last `import`
- Verify host is reading line-delimited messages (`\n` terminated)
- Confirm you are connected to the correct Serial port

### Button state appears inverted

- Verify wiring (INPUT_PULLUP expects GND when pressed)
- Use `invert` only to invert the **keyboard** logic if needed

### Double triggers or noise

- Increase `debounce` time

### Keyboard output not working

- Ensure `keyboard` is `true` in your last `import`
- Verify the `key` string is valid

### Settings not persisting after reboot

- Firmware uses `cfg_ver` in the config file header; when it changes, stored config is reset
- Confirm `export` after reboot matches what you imported

---

## 10. Notes

- Firmware supports up to 32 configured inputs
- Configuration file path: `/tgs_input_config.txt`
- All settings persist across reboots (unless `cfg_ver` changes)

---

End of document
