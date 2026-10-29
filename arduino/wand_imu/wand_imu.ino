/* ===========================================================================
 * wand_imu.ino  -  CEG5205 mixed-reality wand, IMU streaming firmware (v1.0)
 * ---------------------------------------------------------------------------
 * Hardware:  MPU-6050 (GY-521)  ->  Arduino Mega 2560  ->  USB  ->  MacBook
 *
 * Wiring (Mega 2560):
 *   MPU VCC -> 5V        (the GY-521 board has its own 3.3 V regulator)
 *   MPU GND -> GND
 *   MPU SDA -> pin 20    (Mega's dedicated SDA)
 *   MPU SCL -> pin 21    (Mega's dedicated SCL)
 *   MPU AD0 -> GND / unconnected  => I2C address 0x68
 *
 * What it does:
 *   - configures the MPU-6050 by hand (only the Wire library, no 3rd-party lib)
 *   - samples at a fixed 100 Hz using micros() timing (never delay())
 *   - prints one ASCII line per sample with an XOR checksum
 *
 * Line format (one per sample):
 *   $,<seq>,<t_ms>,<ax>,<ay>,<az>,<gx>,<gy>,<gz>*<CS>
 *     seq  : sample counter, wraps at 65535 (lets the PC detect dropped lines)
 *     t_ms : millis() when the sample was taken
 *     a*   : acceleration in g        (3 decimals)
 *     g*   : rotation rate in deg/s   (3 decimals)
 *     CS   : XOR of every character BETWEEN the '$' and the '*'
 *            (that includes the comma right after the '$'), 2 upper-case hex
 *
 * Startup header:
 *   #READY,rate=100,acc=4g,gyro=1000dps,fw=1.0
 *
 * Commands (single characters, sent from the PC):
 *   'R' -> resend the header line
 *   'P' -> pause streaming
 *   'S' -> resume streaming
 *
 * Errors:
 *   #I2C_ERR,<count>   at most once per second, when I2C reads fail
 *   #NO_IMU            once per second, when WHO_AM_I does not answer 0x68
 * =========================================================================*/

#include <Wire.h>
#include <stdlib.h>   // dtostrf()
#include <string.h>   // strlen()

/* ---------------------------------------------------------------------------
 * Output port.
 *
 * Everything is printed through OUT. Today that is Serial = the USB cable.
 * To go wireless later, change this single line to:
 *     #define OUT Serial1
 * Serial1 on the Mega is TX1 = pin 18, RX1 = pin 19, so a radio module
 * (e.g. a CC2530 Zigbee board or an HC-05) can be wired there and the rest of
 * this sketch does not change at all.
 *
 * Commands are always read from Serial (the USB side), so the Mac can still
 * talk to the board even when the data goes out over Serial1.
 * -------------------------------------------------------------------------*/
#define OUT Serial

// ---------------------------------------------------------------------------
// Tunable constants
// ---------------------------------------------------------------------------
const uint32_t BAUD_RATE   = 115200;   // must match the Python side
const uint16_t SAMPLE_HZ   = 100;      // sampling rate (configurable)
const uint32_t I2C_HZ      = 400000;   // 400 kHz fast-mode I2C
const char     FW_VERSION[] = "1.0";

const uint32_t SAMPLE_US   = 1000000UL / SAMPLE_HZ;  // 10000 us at 100 Hz
const uint32_t ERR_REPORT_MS = 1000;   // how often an error line may be sent

// ---------------------------------------------------------------------------
// MPU-6050 registers and scale factors
// ---------------------------------------------------------------------------
const uint8_t MPU_ADDR          = 0x68;
const uint8_t REG_SMPLRT_DIV    = 0x19;
const uint8_t REG_CONFIG        = 0x1A;
const uint8_t REG_GYRO_CONFIG   = 0x1B;
const uint8_t REG_ACCEL_CONFIG  = 0x1C;
const uint8_t REG_ACCEL_XOUT_H  = 0x3B;
const uint8_t REG_PWR_MGMT_1    = 0x6B;
const uint8_t REG_WHO_AM_I      = 0x75;

// Sensitivity for the ranges we select below (from the MPU-6050 datasheet).
const float ACC_LSB_PER_G    = 8192.0f;   // +/- 4 g      -> 8192 LSB per g
const float GYRO_LSB_PER_DPS = 32.8f;     // +/- 1000 dps -> 32.8 LSB per deg/s

// ---------------------------------------------------------------------------
// Runtime state
// ---------------------------------------------------------------------------
static uint16_t sampleSeq      = 0;      // wraps at 65535 all by itself
static bool     streaming      = true;   // 'P' / 'S' toggle this
static bool     imuReady       = false;
static uint16_t i2cErrorCount  = 0;
static uint32_t lastErrReport  = 0;
static uint32_t nextSampleUs   = 0;

// ---------------------------------------------------------------------------
// Low-level I2C helpers
// ---------------------------------------------------------------------------

/** Write one byte to one MPU register. Returns true on success. */
static bool writeReg(uint8_t reg, uint8_t value) {
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(reg);
  Wire.write(value);
  return Wire.endTransmission() == 0;
}

/** Read `count` bytes starting at `reg` into `dst`. Returns true on success. */
static bool readRegs(uint8_t reg, uint8_t *dst, uint8_t count) {
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;      // keep the bus
  if (Wire.requestFrom(MPU_ADDR, count, (uint8_t)true) != count) return false;
  for (uint8_t i = 0; i < count; i++) dst[i] = Wire.read();
  return true;
}

/**
 * Wake the sensor and set the ranges and the low-pass filter.
 * Returns true only if WHO_AM_I answered 0x68 and every write succeeded.
 */
static bool mpuInit() {
  uint8_t who = 0;
  if (!readRegs(REG_WHO_AM_I, &who, 1)) return false;
  if (who != 0x68) return false;

  // Wake up (clear SLEEP) and use the gyro X PLL as the clock: more stable
  // than the internal 8 MHz oscillator.
  if (!writeReg(REG_PWR_MGMT_1, 0x01)) return false;
  delay(10);

  // CONFIG: DLPF_CFG = 3  -> accel bandwidth 44 Hz, gyro bandwidth 42 Hz.
  // With any DLPF enabled the internal sample rate becomes 1 kHz.
  if (!writeReg(REG_CONFIG, 0x03)) return false;

  // SMPLRT_DIV: 1 kHz / (1 + 9) = 100 Hz, matching our software timing.
  if (!writeReg(REG_SMPLRT_DIV, (uint8_t)((1000 / SAMPLE_HZ) - 1))) return false;

  // GYRO_CONFIG: FS_SEL = 2 -> +/- 1000 deg/s.
  // (+/- 250 deg/s saturates on a fast wand swipe, which clips the gesture.)
  if (!writeReg(REG_GYRO_CONFIG, 0x02 << 3)) return false;

  // ACCEL_CONFIG: AFS_SEL = 1 -> +/- 4 g.
  if (!writeReg(REG_ACCEL_CONFIG, 0x01 << 3)) return false;

  delay(10);
  return true;
}

// ---------------------------------------------------------------------------
// Output helpers
// ---------------------------------------------------------------------------

/** Send the header line that tells the PC how the stream is configured. */
static void sendHeader() {
  OUT.print(F("#READY,rate="));
  OUT.print(SAMPLE_HZ);
  OUT.print(F(",acc=4g,gyro=1000dps,fw="));
  OUT.println(FW_VERSION);
}

/** Append ",<value>" with 3 decimals to a C string. */
static void appendFloat(char *buf, float value) {
  size_t len = strlen(buf);
  buf[len++] = ',';
  buf[len]   = '\0';
  dtostrf(value, 0, 3, buf + len);   // width 0 => no padding spaces
}

/**
 * Print one sample line.
 * `payload` holds every character that sits between the '$' and the '*',
 * so the checksum is simply the XOR over that string.
 */
static void sendSample(uint16_t seq, uint32_t tMs,
                       const float *acc, const float *gyr) {
  char payload[128];
  snprintf(payload, sizeof(payload), ",%u,%lu", seq, tMs);
  for (uint8_t i = 0; i < 3; i++) appendFloat(payload, acc[i]);
  for (uint8_t i = 0; i < 3; i++) appendFloat(payload, gyr[i]);

  uint8_t checksum = 0;
  for (size_t i = 0; payload[i] != '\0'; i++) checksum ^= (uint8_t)payload[i];

  char csHex[3];
  snprintf(csHex, sizeof(csHex), "%02X", checksum);

  OUT.print('$');
  OUT.print(payload);
  OUT.print('*');
  OUT.println(csHex);
}

/** Send an error line, but never more often than once per second. */
static void reportErrorThrottled(const __FlashStringHelper *tag, bool withCount) {
  uint32_t now = millis();
  if (now - lastErrReport < ERR_REPORT_MS) return;
  lastErrReport = now;
  OUT.print(tag);
  if (withCount) {
    OUT.print(',');
    OUT.print(i2cErrorCount);
  }
  OUT.println();
}

/** Handle the single-character commands coming from the Mac. */
static void handleCommands() {
  while (Serial.available() > 0) {
    char c = (char)Serial.read();
    switch (c) {
      case 'R': case 'r': sendHeader();     break;   // resend header
      case 'P': case 'p': streaming = false; break;  // pause
      case 'S': case 's': streaming = true;  break;  // resume
      default: break;                                // ignore \r, \n, junk
    }
  }
}

// ---------------------------------------------------------------------------
// Arduino entry points
// ---------------------------------------------------------------------------

void setup() {
  OUT.begin(BAUD_RATE);
  if (&OUT != &Serial) Serial.begin(BAUD_RATE);  // keep USB alive for commands

  Wire.begin();
  Wire.setClock(I2C_HZ);
  Wire.setWireTimeout(3000, true);   // never hang forever on a stuck bus

  imuReady = mpuInit();
  if (imuReady) sendHeader();

  nextSampleUs = micros();
}

void loop() {
  handleCommands();

  // ---- If the IMU is missing, complain once per second and keep retrying ---
  if (!imuReady) {
    reportErrorThrottled(F("#NO_IMU"), false);
    imuReady = mpuInit();
    if (imuReady) sendHeader();
    return;
  }

  // ---- Fixed-rate scheduling with micros() ---------------------------------
  uint32_t now = micros();
  if ((int32_t)(now - nextSampleUs) < 0) return;   // not time yet
  nextSampleUs += SAMPLE_US;
  // If we fell badly behind (e.g. a long USB stall), resynchronise instead of
  // trying to catch up with a burst of samples.
  if ((int32_t)(micros() - nextSampleUs) > (int32_t)(5 * SAMPLE_US)) {
    nextSampleUs = micros() + SAMPLE_US;
  }

  // ---- Read 14 bytes: accel XYZ, temperature, gyro XYZ --------------------
  uint8_t raw[14];
  if (!readRegs(REG_ACCEL_XOUT_H, raw, 14)) {
    i2cErrorCount++;
    reportErrorThrottled(F("#I2C_ERR"), true);
    // A long run of failures usually means the sensor was unplugged.
    if (i2cErrorCount % 100 == 0) imuReady = mpuInit();
    return;                                    // skip this sample
  }

  int16_t rawAcc[3], rawGyr[3];
  rawAcc[0] = (int16_t)((raw[0]  << 8) | raw[1]);
  rawAcc[1] = (int16_t)((raw[2]  << 8) | raw[3]);
  rawAcc[2] = (int16_t)((raw[4]  << 8) | raw[5]);
  // raw[6], raw[7] are the temperature, which we do not use.
  rawGyr[0] = (int16_t)((raw[8]  << 8) | raw[9]);
  rawGyr[1] = (int16_t)((raw[10] << 8) | raw[11]);
  rawGyr[2] = (int16_t)((raw[12] << 8) | raw[13]);

  float acc[3], gyr[3];
  for (uint8_t i = 0; i < 3; i++) {
    acc[i] = (float)rawAcc[i] / ACC_LSB_PER_G;      // g
    gyr[i] = (float)rawGyr[i] / GYRO_LSB_PER_DPS;   // deg/s
  }

  if (streaming) sendSample(sampleSeq, millis(), acc, gyr);
  sampleSeq++;   // uint16_t: wraps from 65535 back to 0 on its own
}
