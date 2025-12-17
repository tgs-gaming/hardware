#include <Bounce2.h>
#include <Keyboard.h>
#include <LittleFS.h>
#include <ArduinoJson.h>

#define FW_NAME "tgs input board"
#define FW_VERSION "json-1.0"
#define CONFIG_VERSION 8
#define CONFIG_PATH "/cfg_v8.txt"

enum Mode : uint8_t { Tap = 0, Hold = 1 };

struct ButtonConfig {
  uint8_t pin;
  uint8_t mode;
  uint8_t invert;
  uint16_t key;
  uint16_t debounceMillis;
  uint16_t tapMillis;
};

struct ButtonRuntime {
  ButtonConfig config;
  Bounce debounce;
  bool holding;
  bool lastRawClosed;
  bool lastLogicalOn;
  bool isTapping;
  uint32_t tapStart;
};

static const uint8_t MAX_BUTTONS = 32;
ButtonRuntime buttons[MAX_BUTTONS];
uint8_t buttonCount = 0;

bool eventsEnabled = false;
bool keyboardEnabled = true;
bool inResponse = false;

uint32_t serialBaud = 9600;

uint32_t pendingMask = 0;
bool pendingClosed[MAX_BUTTONS];

String rx;

static inline uint8_t ctz32(uint32_t v) { return (uint8_t)__builtin_ctz(v); }

static inline char lowerChar(char c) {
  if (c >= 'A' && c <= 'Z') return (char)(c - 'A' + 'a');
  return c;
}

static inline String toLowerCopy(const String& s) {
  String r = s;
  for (uint16_t i = 0; i < r.length(); i++) r.setCharAt(i, lowerChar(r.charAt(i)));
  return r;
}

uint16_t parseKey(const String& in) {
  String s = in;
  s.trim();
  if (!s.length()) return 0;

  if (s.length() == 1) return (uint8_t)s[0];

  s = toLowerCopy(s);

  if (s == "enter") return KEY_RETURN;
  if (s == "tab") return KEY_TAB;
  if (s == "esc") return KEY_ESC;
  if (s == "space") return (uint16_t)' ';
  if (s == "backspace") return KEY_BACKSPACE;
  if (s == "del") return KEY_DELETE;

  if (s == "up") return KEY_UP_ARROW;
  if (s == "down") return KEY_DOWN_ARROW;
  if (s == "left") return KEY_LEFT_ARROW;
  if (s == "right") return KEY_RIGHT_ARROW;

  if (s == "f1") return KEY_F1;
  if (s == "f2") return KEY_F2;
  if (s == "f3") return KEY_F3;
  if (s == "f4") return KEY_F4;
  if (s == "f5") return KEY_F5;
  if (s == "f6") return KEY_F6;
  if (s == "f7") return KEY_F7;
  if (s == "f8") return KEY_F8;
  if (s == "f9") return KEY_F9;
  if (s == "f10") return KEY_F10;
  if (s == "f11") return KEY_F11;
  if (s == "f12") return KEY_F12;

  return 0;
}

void saveConfig() {
  File f = LittleFS.open(CONFIG_PATH, "w");
  if (!f) return;

  f.printf("v%d %u %u %u %lu\n",
    CONFIG_VERSION,
    (unsigned)buttonCount,
    (unsigned)(eventsEnabled ? 1 : 0),
    (unsigned)(keyboardEnabled ? 1 : 0),
    (unsigned long)serialBaud
  );

  for (uint8_t i = 0; i < buttonCount; i++) {
    auto& c = buttons[i].config;
    f.printf("%u %u %u %u %u %u\n",
      (unsigned)c.pin,
      (unsigned)c.mode,
      (unsigned)c.invert,
      (unsigned)c.key,
      (unsigned)c.debounceMillis,
      (unsigned)c.tapMillis
    );
  }

  f.close();
}

void defaults() {
  buttonCount = 0;
  eventsEnabled = false;
  keyboardEnabled = true;
  serialBaud = 9600;
  pendingMask = 0;
}

void applyOne(uint8_t i) {
  auto& b = buttons[i];
  pinMode(b.config.pin, INPUT_PULLUP);
  b.debounce.attach(b.config.pin);
  b.debounce.interval(b.config.debounceMillis);

  b.holding = false;
  b.isTapping = false;
  b.tapStart = 0;

  b.debounce.update();
  bool rawClosed = (b.debounce.read() == LOW);
  bool logicalOn = b.config.invert ? !rawClosed : rawClosed;

  b.lastRawClosed = rawClosed;
  b.lastLogicalOn = logicalOn;
}

void applyAll() {
  for (uint8_t i = 0; i < buttonCount; i++) applyOne(i);
}

bool loadConfig() {
  if (!LittleFS.exists(CONFIG_PATH)) return true;

  File f = LittleFS.open(CONFIG_PATH, "r");
  if (!f) return false;

  String header = f.readStringUntil('\n');
  header.trim();

  int v = 0, cnt = 0, ev = 0, kb = 1;
  unsigned long b = 9600;

  if (sscanf(header.c_str(), "v%d %d %d %d %lu", &v, &cnt, &ev, &kb, &b) != 5) {
    f.close();
    return false;
  }
  if (v != CONFIG_VERSION) {
    f.close();
    return false;
  }

  eventsEnabled = (ev != 0);
  keyboardEnabled = (kb != 0);
  serialBaud = (uint32_t)b;

  buttonCount = 0;

  while (f.available() && buttonCount < MAX_BUTTONS) {
    String line = f.readStringUntil('\n');
    line.trim();
    if (!line.length()) continue;

    int pin, mode, inv;
    unsigned key;
    int deb, tap;

    if (sscanf(line.c_str(), "%d %d %d %u %d %d", &pin, &mode, &inv, &key, &deb, &tap) == 6) {
      auto& c = buttons[buttonCount].config;
      c.pin = (uint8_t)pin;
      c.mode = (uint8_t)mode;
      c.invert = (uint8_t)inv;
      c.key = (uint16_t)key;
      c.debounceMillis = (uint16_t)deb;
      c.tapMillis = (uint16_t)tap;

      buttonCount++;
    }
  }

  f.close();
  applyAll();
  return true;
}

void respondError(const char* cmd, const char* err) {
  Serial.print("{\"ok\":false,\"cmd\":\"");
  Serial.print(cmd);
  Serial.print("\",\"error\":\"");
  Serial.print(err);
  Serial.println("\"}");
}

void respondOk(const char* cmd) {
  Serial.print("{\"ok\":true,\"cmd\":\"");
  Serial.print(cmd);
  Serial.println("\"}");
}

void respondConfigs() {
  Serial.print("{\"ok\":true,\"cmd\":\"configs\",\"data\":{");
  Serial.print("\"board\":\""); Serial.print(FW_NAME); Serial.print("\",");
  Serial.print("\"version\":\""); Serial.print(FW_VERSION); Serial.print("\",");
  Serial.print("\"baud\":"); Serial.print(serialBaud); Serial.print(",");
  Serial.print("\"events\":"); Serial.print(eventsEnabled ? "true" : "false"); Serial.print(",");
  Serial.print("\"keyboard\":"); Serial.print(keyboardEnabled ? "true" : "false"); Serial.print(",");
  Serial.print("\"buttons\":[");
  for (uint8_t i = 0; i < buttonCount; i++) {
    auto& c = buttons[i].config;
    if (i) Serial.print(",");
    Serial.print("{\"pin\":"); Serial.print(c.pin);
    Serial.print(",\"mode\":\""); Serial.print(c.mode == Hold ? "hold" : "tap");
    Serial.print("\",\"invert\":"); Serial.print(c.invert ? "true" : "false");
    Serial.print(",\"key\":"); Serial.print(c.key);
    Serial.print(",\"debounce\":"); Serial.print(c.debounceMillis);
    Serial.print(",\"tap\":"); Serial.print(c.tapMillis);
    Serial.print("}");
  }
  Serial.println("]}}");
}

void emitEvent(uint8_t idx, bool closed) {
  if (!eventsEnabled || !Serial) return;
  if (inResponse) {
    pendingClosed[idx] = closed;
    pendingMask |= (1u << idx);
    return;
  }
  Serial.print("{\"event\":true,\"pin\":");
  Serial.print(buttons[idx].config.pin);
  Serial.print(",\"state\":\"");
  Serial.print(closed ? "closed" : "open");
  Serial.println("\"}");
}

void flushEvents() {
  if (!eventsEnabled || inResponse) return;
  uint32_t m = pendingMask;
  pendingMask = 0;
  while (m) {
    uint8_t i = ctz32(m);
    m &= (m - 1);
    emitEvent(i, pendingClosed[i]);
  }
}

void handleJson(const String& line) {
  JsonDocument doc;
  if (deserializeJson(doc, line)) return;

  const char* cmd = doc["cmd"] | "";
  inResponse = true;

  if (!strcmp(cmd, "configs")) {
    respondConfigs();
  } else if (!strcmp(cmd, "board")) {
    Serial.print("{\"ok\":true,\"cmd\":\"board\",\"board\":\"");
    Serial.print(FW_NAME);
    Serial.println("\"}");
  } else if (!strcmp(cmd, "version")) {
    Serial.print("{\"ok\":true,\"cmd\":\"version\",\"version\":\"");
    Serial.print(FW_VERSION);
    Serial.println("\"}");
  } else if (!strcmp(cmd, "serial")) {
    uint32_t b = doc["baud"] | 0;
    if (b < 300 || b > 3000000) {
      respondError(cmd, "args");
    } else {
      serialBaud = b;
      saveConfig();
      respondOk(cmd);
      Serial.flush();
      Serial.end();
      delay(20);
      Serial.begin(serialBaud);
      delay(10);
    }
  } else if (!strcmp(cmd, "subscribe")) {
    eventsEnabled = doc["enabled"] | false;
    saveConfig();
    respondOk(cmd);
  } else if (!strcmp(cmd, "keyboard")) {
    keyboardEnabled = doc["enabled"] | true;
    saveConfig();
    respondOk(cmd);
  } else if (!strcmp(cmd, "add")) {
    int pin = doc["pin"] | -1;
    if (pin < 0 || pin > 29) {
      respondError(cmd, "args");
    } else {
      uint8_t idx = 0xFF;
      for (uint8_t i = 0; i < buttonCount; i++) if (buttons[i].config.pin == (uint8_t)pin) { idx = i; break; }
      if (idx == 0xFF) {
        if (buttonCount >= MAX_BUTTONS) {
          respondError(cmd, "full");
          goto done;
        }
        idx = buttonCount++;
      }

      auto& c = buttons[idx].config;
      c.pin = (uint8_t)pin;

      String mode = doc["mode"] | "tap";
      
      mode = toLowerCopy(mode);
      c.mode = (mode == "hold") ? Hold : Tap;

      c.invert = (doc["invert"] | false) ? 1 : 0;

      String keyStr = doc["key"] | "";
      
      uint16_t k = parseKey(keyStr);
      if (!k) {
        buttonCount = (idx == buttonCount - 1) ? (uint8_t)(buttonCount - 1) : buttonCount;
        respondError(cmd, "key");
        goto done;
      }
      c.key = k;

      c.debounceMillis = (uint16_t)(doc["debounce"] | 8);
      c.tapMillis = (uint16_t)(doc["tap"] | 40);

      applyOne(idx);
      saveConfig();
      respondOk(cmd);
    }
  } else {
    respondError(cmd, "cmd");
  }

done:
  inResponse = false;
  flushEvents();
}

void processSerial() {
  while (Serial.available()) {
    char c = (char)Serial.read();
    if (c == '\n') {
      String l = rx;
      rx = "";
      l.trim();
      if (l.length()) handleJson(l);
    } else if (c != '\r') {
      if (rx.length() < 512) rx += c;
    }
  }
}

void processButtons() {
  uint32_t now = millis();
  for (uint8_t i = 0; i < buttonCount; i++) {
    auto& b = buttons[i];
    b.debounce.update();

    if (b.isTapping && (uint32_t)(now - b.tapStart) >= b.config.tapMillis) {
      Keyboard.release((uint8_t)b.config.key);
      b.isTapping = false;
    }

    bool rawClosed = (b.debounce.read() == LOW);
    bool logicalOn = b.config.invert ? !rawClosed : rawClosed;

    if (rawClosed != b.lastRawClosed) {
      b.lastRawClosed = rawClosed;
      emitEvent(i, rawClosed);
    }

    if (!keyboardEnabled) {
      b.lastLogicalOn = logicalOn;
      continue;
    }

    if (logicalOn != b.lastLogicalOn) {
      b.lastLogicalOn = logicalOn;

      if (logicalOn) {
        if (b.config.mode == Tap) {
          Keyboard.press((uint8_t)b.config.key);
          b.tapStart = now;
          b.isTapping = true;
        } else {
          b.holding = true;
          Keyboard.press((uint8_t)b.config.key);
        }
      } else {
        if (b.holding) {
          b.holding = false;
          Keyboard.release((uint8_t)b.config.key);
        }
      }
    }
  }
}

void setup() {
  rx.reserve(256);

  defaults();

  if (!LittleFS.begin()) {
    LittleFS.format();
    LittleFS.begin();
  }

  Keyboard.begin();

  loadConfig();
  Serial.begin(serialBaud);
  delay(10);
}

void loop() {
  processButtons();
  processSerial();
  flushEvents();
}