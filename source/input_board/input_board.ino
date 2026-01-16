#include <Bounce2.h>
#include <Keyboard.h>
#include <LittleFS.h>
#include <ArduinoJson.h>

#define FW_NAME "TGS Input Board"
#define FW_VERSION "1.2"
#define CONFIG_VERSION 1
#define CONFIG_PATH "/tgs_input_config.txt"

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

static inline void printJsonEscaped(const String& s) {
  for (uint16_t i = 0; i < s.length(); i++) {
    char c = s.charAt(i);
    switch (c) {
      case '\"': Serial.print("\\\""); break;
      case '\\': Serial.print("\\\\"); break;
      case '\b': Serial.print("\\b"); break;
      case '\f': Serial.print("\\f"); break;
      case '\n': Serial.print("\\n"); break;
      case '\r': Serial.print("\\r"); break;
      case '\t': Serial.print("\\t"); break;
      default:
        if ((uint8_t)c < 0x20) {
          Serial.print("\\u00");
          const char* hex = "0123456789abcdef";
          Serial.print(hex[((uint8_t)c >> 4) & 0xF]);
          Serial.print(hex[((uint8_t)c >> 0) & 0xF]);
        } else {
          Serial.print(c);
        }
        break;
    }
  }
}

static inline String keyToString(uint16_t k) {
  if (!k) return "";
  if (k == (uint16_t)' ') return "space";

  switch (k) {
    case KEY_RETURN: return "enter";
    case KEY_TAB: return "tab";
    case KEY_ESC: return "esc";
    case KEY_BACKSPACE: return "backspace";
    case KEY_DELETE: return "del";
    case KEY_UP_ARROW: return "up";
    case KEY_DOWN_ARROW: return "down";
    case KEY_LEFT_ARROW: return "left";
    case KEY_RIGHT_ARROW: return "right";
    case KEY_F1: return "f1";
    case KEY_F2: return "f2";
    case KEY_F3: return "f3";
    case KEY_F4: return "f4";
    case KEY_F5: return "f5";
    case KEY_F6: return "f6";
    case KEY_F7: return "f7";
    case KEY_F8: return "f8";
    case KEY_F9: return "f9";
    case KEY_F10: return "f10";
    case KEY_F11: return "f11";
    case KEY_F12: return "f12";
  }

  if (k >= 32 && k <= 126) {
    String s;
    s += (char)k;
    return s;
  }

  String s = "code:";
  s += String((unsigned)k);
  return s;
}

uint16_t parseKey(const String& in) {
  String s = in;
  s.trim();
  if (!s.length()) return 0;

  if (s.length() >= 5) {
    String p = s.substring(0, 5);
    p = toLowerCopy(p);
    if (p == "code:") {
      String n = s.substring(5);
      n.trim();
      bool ok = true;
      for (uint16_t i = 0; i < n.length(); i++) {
        char c = n.charAt(i);
        if (c < '0' || c > '9') { ok = false; break; }
      }
      if (ok && n.length()) {
        unsigned long v = n.toInt();
        if (v > 0 && v <= 65535) return (uint16_t)v;
      }
      return 0;
    }
  }

  bool allDigits = true;
  for (uint16_t i = 0; i < s.length(); i++) {
    char c = s.charAt(i);
    if (c < '0' || c > '9') { allDigits = false; break; }
  }
  if (allDigits) {
    unsigned long v = s.toInt();
    if (v > 0 && v <= 65535) return (uint16_t)v;
    return 0;
  }

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

bool parseModeStr(const String& in, uint8_t& outMode) {
  String s = toLowerCopy(in);
  if (s == "tap") { outMode = Tap; return true; }
  if (s == "hold") { outMode = Hold; return true; }
  return false;
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

  f.flush();
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

void resetConfigStorage() {
  defaults();
  LittleFS.remove(CONFIG_PATH);
  saveConfig();
  applyAll();
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
  Serial.print("{\"type\":\"resp\",\"ok\":false,\"cmd\":\"");
  Serial.print(cmd);
  Serial.print("\",\"error\":\"");
  Serial.print(err);
  Serial.println("\"}");
}

void respondOk(const char* cmd) {
  Serial.print("{\"type\":\"resp\",\"ok\":true,\"cmd\":\"");
  Serial.print(cmd);
  Serial.println("\"}");
}

void respondBoard() {
  Serial.print("{\"type\":\"resp\",\"ok\":true,\"cmd\":\"board\",\"board\":\"");
  Serial.print(FW_NAME);
  Serial.println("\"}");
}

void respondVersion() {
  Serial.print("{\"type\":\"resp\",\"ok\":true,\"cmd\":\"version\",\"version\":\"");
  Serial.print(FW_VERSION);
  Serial.println("\"}");
}

void respondExport() {
  Serial.print("{\"type\":\"resp\",\"ok\":true,\"cmd\":\"export\",\"data\":{");
  Serial.print("\"schema\":1,");
  Serial.print("\"cfg_ver\":"); Serial.print(CONFIG_VERSION); Serial.print(",");
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
    Serial.print(",\"key\":\"");
    String ks = keyToString(c.key);
    printJsonEscaped(ks);
    Serial.print("\"");
    Serial.print(",\"debounce\":"); Serial.print(c.debounceMillis);
    Serial.print(",\"tap\":"); Serial.print(c.tapMillis);
    Serial.print("}");
  }
  Serial.println("]}}");
}

bool applyImport(JsonVariant dataVar, uint32_t& newBaudOut, bool& baudChangedOut) {
  if (dataVar.isNull() || !dataVar.is<JsonObject>()) return false;

  JsonObject data = dataVar.as<JsonObject>();

  uint32_t newBaud = data["baud"] | serialBaud;
  if (newBaud < 300 || newBaud > 3000000) return false;

  bool newEvents = data["events"] | eventsEnabled;
  bool newKeyboard = data["keyboard"] | keyboardEnabled;

  JsonArray btns = data["buttons"].as<JsonArray>();
  if (btns.isNull()) return false;
  if (btns.size() > MAX_BUTTONS) return false;

  ButtonConfig temp[MAX_BUTTONS];
  uint8_t tempCount = 0;

  for (JsonVariant v : btns) {
    if (!v.is<JsonObject>()) return false;
    JsonObject o = v.as<JsonObject>();

    int pin = o["pin"] | -1;
    if (pin < 0 || pin > 29) return false;

    String modeStr = (const char*)(o["mode"] | "tap");
    uint8_t mode;
    if (!parseModeStr(modeStr, mode)) return false;

    bool inv = o["invert"] | false;

    String keyStr = (const char*)(o["key"] | "");
    uint16_t key = parseKey(keyStr);
    if (!key) return false;

    uint16_t deb = (uint16_t)(o["debounce"] | 8);
    uint16_t tap = (uint16_t)(o["tap"] | 40);

    temp[tempCount].pin = (uint8_t)pin;
    temp[tempCount].mode = mode;
    temp[tempCount].invert = inv ? 1 : 0;
    temp[tempCount].key = key;
    temp[tempCount].debounceMillis = deb;
    temp[tempCount].tapMillis = tap;
    tempCount++;
  }

  uint32_t oldBaud = serialBaud;

  buttonCount = tempCount;
  for (uint8_t i = 0; i < buttonCount; i++) buttons[i].config = temp[i];

  eventsEnabled = newEvents;
  keyboardEnabled = newKeyboard;
  serialBaud = newBaud;

  pendingMask = 0;

  applyAll();
  saveConfig();

  baudChangedOut = (oldBaud != newBaud);
  newBaudOut = newBaud;
  return true;
}

void emitEvent(uint8_t idx, bool closed) {
  if (!eventsEnabled || !Serial) return;
  if (inResponse) {
    pendingClosed[idx] = closed;
    pendingMask |= (1u << idx);
    return;
  }
  Serial.print("{\"type\":\"event\",\"pin\":");
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

  if (!strcmp(cmd, "export")) {
    respondExport();

  } else if (!strcmp(cmd, "import")) {
    uint32_t newBaud = serialBaud;
    bool baudChanged = false;

    JsonVariant data = doc["data"];
    if (!applyImport(data, newBaud, baudChanged)) {
      respondError(cmd, "args");
    } else {
      respondOk(cmd);
      if (baudChanged) {
        Serial.flush();
        Serial.end();
        delay(20);
        Serial.begin(newBaud);
        delay(10);
      }
    }

  } else if (!strcmp(cmd, "board")) {
    respondBoard();

  } else if (!strcmp(cmd, "version")) {
    respondVersion();

  } else if (!strcmp(cmd, "reset")) {
    resetConfigStorage();
    respondOk(cmd);

  } else {
    respondError(cmd, "cmd");
  }

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

  if (!loadConfig()) {
    resetConfigStorage();
  }

  Serial.begin(serialBaud);
  delay(10);
}

void loop() {
  processButtons();
  processSerial();
  flushEvents();
}
