/*
  (fully reworked version for Miluva by Thilo M)

  "Firmata is a generic protocol for communicating with microcontrollers
  from software on a host computer. It is intended to work with
  any host computer software package"

  See file LICENSE.txt for further informations on licensing terms.
*/

//#include <Servo.h>
#include <Wire.h>
#include <Firmata.h>
#include <Stepper.h>
#include "MD_Parola.h"
#include "MD_MAX72xx.h"
#include "SPI.h"

#define HARDWARE_TYPE MD_MAX72XX::FC16_HW
#define MAX_DEVICES 4
#define CS_PIN 7

// hardware SPI connection:
//MD_Parola myDisplay = MD_Parola(HARDWARE_TYPE, CS_PIN, MAX_DEVICES);

#define DATAPIN 4
#define CLK_PIN 8
MD_Parola myDisplay = MD_Parola(HARDWARE_TYPE, DATAPIN, CLK_PIN, CS_PIN, MAX_DEVICES);

#define stepPin 2
#define stepPin2 6
#define dirPin 5 
#define dirPin2 3
#define btnPin 0

// the minimum interval for sampling analog input
#define MINIMUM_SAMPLING_INTERVAL   1


/*==============================================================================
 * GLOBAL VARIABLES
 *============================================================================*/

#ifdef FIRMATA_SERIAL_FEATURE
SerialFirmata serialFeature;
#endif

unsigned long millisA1Press = 0, millisA2Press = 0, animMillis = 0;
bool a1Press = false, a2Press = false, anyinputthispanel = false;
byte CUR_PANEL = 0, SETUP_CUR_PANEL = -1;
int animCount = 0, tinput;
byte hIdx = 0, SmallUniversalVal = 0;

unsigned long remainingMillis = 5400000, timeStamp = 0, lastTimeStamp = 0;
bool TIMER_SIDE = false, IS_HUMANS_TURN = false;

const int MAX_RPM = 200; // Für die 12V Stepper

#define MOTOR_RPM_5V 15
#define QUATERROT_STEPS_5V 1019 //509.5; 2038 für eine volle Umdrehung
Stepper stepper(2038, 10, 12, 11, 13); // 5V Stepper

/* analog inputs */
int analogInputsToReport = 0; // bitwise array to store pin reporting

/* digital input ports */
byte reportPINs[TOTAL_PORTS];       // 1 = report this port, 0 = silence
byte previousPINs[TOTAL_PORTS];     // previous 8 bits sent

/* pins configuration */
byte portConfigInputs[TOTAL_PORTS]; // each bit: 1 = pin in INPUT, 0 = anything else

/* timer variables */
unsigned long currentMillis;        // store the current value from millis()
unsigned long previousMillis;       // for comparison with currentMillis
unsigned int samplingInterval = 19; // how often to run the main loop (in ms)

/* i2c data */
struct i2c_device_info {
  byte addr;
  int reg;
  byte bytes;
  byte stopTX;
};

/* for i2c read continuous more */
//i2c_device_info query[I2C_MAX_QUERIES];

byte i2cRxData[64];
boolean isI2CEnabled = false;
signed char queryIndex = -1;
// default delay time between i2c read request and Wire.requestFrom()
unsigned int i2cReadDelayTime = 0;

boolean isResetting = false;

// Forward declare a few functions to avoid compiler errors with older versions
// of the Arduino IDE.
void setPinModeCallback(byte, int);
void reportAnalogCallback(byte analogPin, int value);
void sysexCallback(byte, byte, byte*);

/* utility functions */
void wireWrite(byte data)
{
#if ARDUINO >= 100
  Wire.write((byte)data);
#else
  Wire.send(data);
#endif
}

byte wireRead(void)
{
#if ARDUINO >= 100
  return Wire.read();
#else
  return Wire.receive();
#endif
}

void readAndReportData(byte address, int theRegister, byte numBytes, byte stopTX) {

}

void outputPort(byte portNumber, byte portValue, byte forceSend)
{
  // pins not configured as INPUT are cleared to zeros
  portValue = portValue & portConfigInputs[portNumber];
  // only send if the value is different than previously sent
  if (forceSend || previousPINs[portNumber] != portValue) {
    Firmata.sendDigitalPort(portNumber, portValue);
    previousPINs[portNumber] = portValue;
  }
}

/* -----------------------------------------------------------------------------
 * check all the active digital inputs for change of state, then add any events
 * to the Serial output queue using Serial.print() */
void checkDigitalInputs(void)
{

}

// -----------------------------------------------------------------------------
/* sets the pin mode to the correct state and sets the relevant bits in the
 * two bit-arrays that track Digital I/O and PWM status
 */
void setPinModeCallback(byte pin, int mode)
{
  if (Firmata.getPinMode(pin) == PIN_MODE_IGNORE)
    return;

  //if (Firmata.getPinMode(pin) == PIN_MODE_I2C && isI2CEnabled && mode != PIN_MODE_I2C) {
    // disable i2c so pins can be used for other functions
    // the following if statements should reconfigure the pins properly
    //disableI2CPins();
  //}
  //if (IS_PIN_DIGITAL(pin) && mode != PIN_MODE_SERVO) {
  //  if (servoPinMap[pin] < MAX_SERVOS && servos[servoPinMap[pin]].attached()) {
  //    detachServo(pin);
  //  }
  //}
  //if (IS_PIN_ANALOG(pin)) {
  //  reportAnalogCallback(PIN_TO_ANALOG(pin), mode == PIN_MODE_ANALOG ? 1 : 0); // turn on/off reporting
  //}
  if (IS_PIN_DIGITAL(pin)) {
    if (mode == INPUT || mode == PIN_MODE_PULLUP) {
      portConfigInputs[pin / 8] |= (1 << (pin & 7));
    } else {
      portConfigInputs[pin / 8] &= ~(1 << (pin & 7));
    }
  }
  Firmata.setPinState(pin, 0);
  switch (mode) {
    case PIN_MODE_ANALOG:
      if (IS_PIN_ANALOG(pin)) {
        if (IS_PIN_DIGITAL(pin)) {
          pinMode(PIN_TO_DIGITAL(pin), INPUT);    // disable output driver
#if ARDUINO <= 100
          // deprecated since Arduino 1.0.1 - TODO: drop support in Firmata 2.6
          digitalWrite(PIN_TO_DIGITAL(pin), LOW); // disable internal pull-ups
#endif
        }
        Firmata.setPinMode(pin, PIN_MODE_ANALOG);
      }
      break;
      
    case INPUT:
      if (IS_PIN_DIGITAL(pin)) {
        pinMode(PIN_TO_DIGITAL(pin), INPUT);    // disable output driver
#if ARDUINO <= 100
        // deprecated since Arduino 1.0.1 - TODO: drop support in Firmata 2.6
        digitalWrite(PIN_TO_DIGITAL(pin), LOW); // disable internal pull-ups
#endif
        Firmata.setPinMode(pin, INPUT);
      }
      break;/*
    case PIN_MODE_PULLUP:
      if (IS_PIN_DIGITAL(pin)) {
        pinMode(PIN_TO_DIGITAL(pin), INPUT_PULLUP);
        Firmata.setPinMode(pin, PIN_MODE_PULLUP);
        Firmata.setPinState(pin, 1);
      }
      break;*/
    case OUTPUT:
      if (IS_PIN_DIGITAL(pin)) {
        if (Firmata.getPinMode(pin) == PIN_MODE_PWM) {
          // Disable PWM if pin mode was previously set to PWM.
          digitalWrite(PIN_TO_DIGITAL(pin), LOW);
        }
        pinMode(PIN_TO_DIGITAL(pin), OUTPUT);
        Firmata.setPinMode(pin, OUTPUT);
      }
      break;
    case PIN_MODE_I2C:
      if (IS_PIN_I2C(pin)) {
        // mark the pin as i2c
        // the user must call I2C_CONFIG to enable I2C for a device
        Firmata.setPinMode(pin, PIN_MODE_I2C);
      }
      break;
    case PIN_MODE_SERIAL:
#ifdef FIRMATA_SERIAL_FEATURE
      serialFeature.handlePinMode(pin, PIN_MODE_SERIAL);
#endif
      break;
    //default:
      //Firmata.sendString("Unknown pin mode"); // TODO: put error msgs in EEPROM
  }
  // TODO: save status to EEPROM here, if changed
}

int dataCount = 0;
long actualData = 0;
long actionQueue[125];
int countOfQActions;
bool curMagnetState = false;

void setPinValueCallback(byte pin, int value)
{
  if (pin < TOTAL_PINS && IS_PIN_DIGITAL(pin)) {
    if (Firmata.getPinMode(pin) == OUTPUT) {
      Firmata.setPinState(pin, value);
      if (pin == 9) {
          //if (dataCount == 0) digitalWrite(10, HIGH);
          actualData = actualData | ((long)value << dataCount++);
      } 
      else if (pin == 10) {
        //processCustomNumberData();
        if (value == 0) {
          actionQueue[countOfQActions++] = actualData;
          dataCount = actualData = 0;
        }
        else {
          executeAllEnqueuedActions();
        }
      }
      else digitalWrite(PIN_TO_DIGITAL(pin), value);
    }
  }
}

void processCustomNumberData(long pData) {

  int curActionType = pData & 7; // 8 Options
  int delayAfterAction = (pData >> 4) & 255; // 0 to 255ms
  bool bInformation = (pData >> 3) & 1; // Motor / State specifications

  long remainingData = pData >> 12; 

  switch (curActionType) {

    case 0: // 0 => TURN WITH MOVEMENT SPECIFICATIONS [% ******** *]

      TURN(
        remainingData >> 9, // Steps
        (remainingData >> 1) & 255,  // RPM
        remainingData & 1, // Direction
        bInformation // Axis
      );
      break;

    case 4: // 0 => TURN WITH MOVEMENT SPECIFICATIONS AND ADDED 1024 rotations [% ******** *]

      TURN(
        (remainingData >> 9) + 1024, // Steps
        (remainingData >> 1) & 255,  // RPM
        remainingData & 1, // Direction
        bInformation // Axis
      );
      break;


    case 1: // 1 => MOVE UNTIL BUTTON SIGNAL [% *** *]
      TURN_UNTIL_BTN_PRESS(
        (remainingData >> 1) & 7, // Button Signal Pin
        remainingData >> 4, // RPM
        remainingData & 1, // Direction
        bInformation
      ); 
      break;


    case 2: // 2 => SET MAGNET STATE []
      ChangeMagnetState(bInformation);
      break;


    case 3:




      /*switch (delayAfterAction & 7) {

        case 0: // Clock until Signal

        break;

        case 1: // Clock until Btn Press

        break;

        case 2: // Promotion Menu

        break;

        case 3: // Promotion Text (+ Clock Info)

        break;

        case 4: // End of Game Text

        break;

        case 5: // with Btn Press; lets go! (+ Clock Info) [Which Side, Is Human Next, RemainingMillis]

        break;

      }*/

      CUR_PANEL = delayAfterAction & 7;
      millisA2Press = millisA1Press = 0;
      Firmata.setPinState(9, anyinputthispanel = LOW);
      IS_HUMANS_TURN = remainingData & 1;
      TIMER_SIDE = remainingData & 2;
      remainingMillis = ((remainingData & 1048575) >> 2) * 100;
      SmallUniversalVal = delayAfterAction >> 3;

      // delayAfterAction >> 3 -> UNIVERSAL SMALL VALUE
      // bInformation -> UNIVERSAL BOOL


      // remainingData & 1 -> CLOCK SIDE
      // remainingData & 2 -> HUMAN
      // remainingData >> 2 -> TIME

      //if (delayAfterAction == 0) {
        // Clock until Signal
      //}
      //else if (delayAfterAction == 1) 

      //delayAfterAction
      //remainingData

      break;

  }

  if (curActionType != 3) delay(delayAfterAction);
}

void ChangeMagnetState(bool pState) {
  //if (pState == curMagnetState) return;

  curMagnetState = pState;
  stepper.setSpeed(MOTOR_RPM_5V);
  stepper.step((pState ? -1 : 1) * QUATERROT_STEPS_5V);
}

void executeAllEnqueuedActions() {
  for (int i = 0; i < countOfQActions; i++ ) {
    processCustomNumberData(actionQueue[i]);
  }
  countOfQActions = 0;
  Firmata.setPinState(4, HIGH);
  //digitalWrite(10, HIGH);
}

int Clamp(int pVal, int pMin, int pMax) {
  if (pVal < pMin) return pMin;
  else if (pVal > pMax) return pMax;
  return pVal;
}

void TURN_UNTIL_BTN_PRESS(int pAnlgBtnPin, int pRPM, bool pClockwise, bool pMainAxis) {
  int microSecondDelay = CALC_MICROSEC_DELAY(pRPM);
  digitalWrite(pMainAxis ? dirPin2 : dirPin, !pClockwise);
  while (analogRead(pAnlgBtnPin) < 100) STEPPER_PULSE(pMainAxis ? stepPin2 : stepPin, microSecondDelay);
}

void TURN(int pSteps, int pRPM, bool pClockwise, bool pMainAxis) {
  //digitalWrite(testLEDPin, HIGH);
  int microSecondDelay = CALC_MICROSEC_DELAY(pRPM);
  digitalWrite(pMainAxis ? dirPin2 : dirPin, !pClockwise);
  for(int c = 0; c < pSteps; c++) STEPPER_PULSE(pMainAxis ? stepPin2 : stepPin, microSecondDelay);
  //digitalWrite(testLEDPin, LOW);
}

int CALC_MICROSEC_DELAY(int pRPM) {
  return (int)(30000000 / ((double)Clamp(pRPM, 1, MAX_RPM) * 200.0));
}

void STEPPER_PULSE(int pPin, int pDelay) {
  digitalWrite(pPin, HIGH); 
  delayMicroseconds(pDelay);
  digitalWrite(pPin, LOW); 
  delayMicroseconds(pDelay); 
}


void digitalWriteCallback(byte port, int value)
{
  byte pin, lastPin, pinValue, mask = 1, pinWriteMask = 0;

  if (port < TOTAL_PORTS) {
    // create a mask of the pins on this port that are writable.
    lastPin = port * 8 + 8;
    if (lastPin > TOTAL_PINS) lastPin = TOTAL_PINS;
    for (pin = port * 8; pin < lastPin; pin++) {
      // do not disturb non-digital pins (eg, Rx & Tx)
      if (IS_PIN_DIGITAL(pin)) {
        // do not touch pins in PWM, ANALOG, SERVO or other modes
        if (Firmata.getPinMode(pin) == OUTPUT || Firmata.getPinMode(pin) == INPUT) {
          pinValue = ((byte)value & mask) ? 1 : 0;
          if (Firmata.getPinMode(pin) == OUTPUT) {
            pinWriteMask |= mask;
          } else if (Firmata.getPinMode(pin) == INPUT && pinValue == 1 && Firmata.getPinState(pin) != 1) {
            // only handle INPUT here for backwards compatibility
#if ARDUINO > 100
            pinMode(pin, INPUT_PULLUP);
#else
            // only write to the INPUT pin to enable pullups if Arduino v1.0.0 or earlier
            pinWriteMask |= mask;
#endif
          }
          Firmata.setPinState(pin, pinValue);
        }
      }
      mask = mask << 1;
    }
    writePort(port, (byte)value, pinWriteMask);
  }
}

void reportAnalogCallback(byte analogPin, int value)
{
}

void reportDigitalCallback(byte port, int value)
{
}

/*==============================================================================
 * SYSEX-BASED commands
 *============================================================================*/

void sysexCallback(byte command, byte argc, byte *argv)
{
  switch (command) {
    case PIN_STATE_QUERY:
      if (argc > 0) {
        byte pin = argv[0];
        Firmata.write(START_SYSEX);
        Firmata.write(PIN_STATE_RESPONSE);
        Firmata.write(pin);
        if (pin < TOTAL_PINS) {
          Firmata.write(Firmata.getPinMode(pin));
          Firmata.write((byte)Firmata.getPinState(pin) & 0x7F);
          if (Firmata.getPinState(pin) & 0xFF80) Firmata.write((byte)(Firmata.getPinState(pin) >> 7) & 0x7F);
          if (Firmata.getPinState(pin) & 0xC000) Firmata.write((byte)(Firmata.getPinState(pin) >> 14) & 0x7F);
        }
        Firmata.write(END_SYSEX);
      }
      break;
  }
}

/*==============================================================================
 * SETUP()
 *============================================================================*/

void systemResetCallback()
{
  isResetting = true;

  // initialize a defalt state
  // TODO: option to load config from EEPROM instead of default

#ifdef FIRMATA_SERIAL_FEATURE
  serialFeature.reset();
#endif

  //if (isI2CEnabled) {
  //  disableI2CPins();
  // }

  for (byte i = 0; i < TOTAL_PORTS; i++) {
    reportPINs[i] = false;    // by default, reporting off
    portConfigInputs[i] = 0;  // until activated
    previousPINs[i] = 0;
  }

  for (byte i = 0; i < TOTAL_PINS; i++) {
    // pins with analog capability default to analog input
    // otherwise, pins default to digital output
    if (IS_PIN_ANALOG(i)) {
      // turns off pullup, configures everything
      setPinModeCallback(i, PIN_MODE_ANALOG);
    } else if (IS_PIN_DIGITAL(i)) {
      // sets the output to 0, configures portConfigInputs
      setPinModeCallback(i, OUTPUT);
    }

    //servoPinMap[i] = 255;
  }
  // by default, do not report any analog inputs
  analogInputsToReport = 0;
  isResetting = false;
}

void setup()
{
  Firmata.setFirmwareVersion(FIRMATA_FIRMWARE_MAJOR_VERSION, FIRMATA_FIRMWARE_MINOR_VERSION);

  //Firmata.attach(ANALOG_MESSAGE, analogWriteCallback);
  Firmata.attach(DIGITAL_MESSAGE, digitalWriteCallback);
  Firmata.attach(REPORT_ANALOG, reportAnalogCallback);
  Firmata.attach(REPORT_DIGITAL, reportDigitalCallback);
  Firmata.attach(SET_PIN_MODE, setPinModeCallback);
  Firmata.attach(SET_DIGITAL_PIN_VALUE, setPinValueCallback);
  Firmata.attach(START_SYSEX, sysexCallback);
  Firmata.attach(SYSTEM_RESET, systemResetCallback);

  Firmata.begin(57600);
  while (!Serial) {
    ; // wait for serial port to connect. Needed for ATmega32u4-based boards and Arduino 101
  }

  systemResetCallback();  // reset to default config

  myDisplay.begin();
  myDisplay.setIntensity(0);
  myDisplay.displayClear();

  Firmata.setPinState(3, LOW);
}


void firmataLoop() {



  byte pin, analogPin;

  /* DIGITALREAD - as fast as possible, check for changes and output them to the
   * FTDI buffer using Serial.print()  */
  checkDigitalInputs();

  /* STREAMREAD - processing incoming messagse as soon as possible, while still
   * checking digital inputs.  */
  while (Firmata.available())
    Firmata.processInput();

  currentMillis = millis();
  if (currentMillis - previousMillis > samplingInterval) {
    previousMillis += samplingInterval;
    /* ANALOGREAD - do all analogReads() at the configured sampling interval */
    for (pin = 0; pin < TOTAL_PINS; pin++) {
      if (IS_PIN_ANALOG(pin) && Firmata.getPinMode(pin) == PIN_MODE_ANALOG) {
        analogPin = PIN_TO_ANALOG(pin);
        if (analogInputsToReport & (1 << analogPin)) {
          Firmata.sendAnalog(analogPin, analogRead(analogPin));
        }
      }
    }
  }

#ifdef FIRMATA_SERIAL_FEATURE
  serialFeature.update();
#endif
}

void btncontrolpanelcheck() {

  unsigned long curTime = millis();

  if (analogRead(1) < 250) {
    if (!a1Press) millisA1Press = curTime;
    a1Press = true;
  }
  else a1Press = false;

  if (analogRead(2) < 250) {
    if (!a2Press) millisA2Press = curTime;
    a2Press = true;
  }
  else a2Press = false;

  int ta1 = curTime - millisA1Press, ta2 = curTime - millisA2Press;

  if (millisA1Press == 0) ta1 = 0;
  if (millisA2Press == 0) ta2 = 0;

  int previnp = tinput;

  tinput = 0;
  if (a1Press && a2Press && ta1 > 250 && ta2 > 250) tinput = 3;
  else if (previnp == 3 || previnp == -1)  {
    if (a1Press || a2Press) tinput = -1; 
  }
  else if (a1Press && !a2Press && ta1 > 50) tinput = 1;
  else if (a2Press && !a1Press && ta2 > 50) tinput = 2;

  if (tinput > 0 && CUR_PANEL != 4) Firmata.setPinState(9, anyinputthispanel = HIGH);
  
  digitalWrite(9, tinput > 0 && CUR_PANEL != 4);
}

/*

0: Startup; waiting for Settings from PC (until Signal)
1: Press Button to Start (until Btn Press )
2: Clock (Right / Left) -> until Btn Press 
3: Clock (Right / Left) -> until Signal
4: Interactive Promotion Selection Menü (Until Double Btn Press)
5: Promotion Text Opp (until Btn Press)
6: Win / Loose Text (until Btn Press) 

*/

const char* helloArr[] = {
  "Hello", "Hallo", "Hola", "Ciao", "Hej"
};
const char* promTypeArr[] = {
  "Queen", "Rook", "Bishop", "Knight"
};
const char* gameResultArr[] = {
  "White Wins!", "Draw!", "Black Wins!"
};
const char* waitingAnim[] = {
  "/", "|", "\\", "--"
};

void DisplayTimer() {
  myDisplay.setTextAlignment(TIMER_SIDE ? PA_LEFT : PA_RIGHT);

  long ttVal = remainingMillis + timeStamp;
  long tVal = ttVal - millis();

  if (remainingMillis == 10000000) tVal = millis() - timeStamp;

  if (anyinputthispanel && IS_HUMANS_TURN) tVal = lastTimeStamp;
  else lastTimeStamp = tVal;

  if (millis() > ttVal) {
    myDisplay.print("00:00");
    return;
  }

  unsigned long secondsR = tVal % 60000;
  unsigned long minutes = (tVal - secondsR) / 60000;
  unsigned long seconds = (secondsR - secondsR % 1000) / 1000;

  String doppelpunkt = (seconds < 10 ? ":0" : ":"); // Der Datentyp verändert sich sonst beim concat-Verfahren 
                            // von der Arduino Sprache; dann nutzt das ganze glaub ich irgnen 
                            // Memory Hash anstelle von dem Text den ich zeigen möchte
  String leadingzero = (minutes < 10 ? "0" : "");
  myDisplay.print(leadingzero + minutes + doppelpunkt + seconds);
}
void loop()
{
  firmataLoop();
  btncontrolpanelcheck();

  if (myDisplay.displayAnimate()) {
    animCount++;
    myDisplay.displayReset();
  }

  int newPanel = 100;
  switch (CUR_PANEL) {
    case 0:
      if (animCount == 1 || SETUP_CUR_PANEL != CUR_PANEL) {

        animCount = 0;
        if (++hIdx > 4) hIdx = 0;
        myDisplay.displayText(helloArr[hIdx], PA_CENTER, 20, 5000, PA_RANDOM, PA_SCROLL_DOWN);

      }

    break;

    case 1:

      if (SETUP_CUR_PANEL != CUR_PANEL) {
        Firmata.setPinState(3, LOW);
        myDisplay.displayClear();
        myDisplay.displayText("Press To Start", PA_CENTER, 100, 0, PA_SCROLL_LEFT, PA_SCROLL_LEFT);

      }

      if (tinput > 0) {
        newPanel = 2;
        Firmata.setPinState(3, HIGH);
      } 

    break;

    case 2:

    if (SETUP_CUR_PANEL != CUR_PANEL) {

      animCount = 1;
      myDisplay.displayClear();
      timeStamp = millis();

    }

    DisplayTimer();

    break;
    
    case 3:

      if (SETUP_CUR_PANEL != CUR_PANEL) {
        myDisplay.displayClear();
        myDisplay.setTextAlignment(PA_CENTER);
      }

      myDisplay.print("Error?");

      if (tinput) Firmata.setPinState(5, HIGH);

    break;

    case 4:

      if (SETUP_CUR_PANEL != CUR_PANEL) {
        
        Firmata.setPinState(10, LOW);

        hIdx = 0;
        myDisplay.displayClear();

        myDisplay.setTextAlignment(PA_CENTER);
        myDisplay.print(promTypeArr[0]);

      }
      
      myDisplay.print(promTypeArr[hIdx]);

      switch (tinput) {
        case 1:
          millisA1Press = 0;
          if (++hIdx > 3) hIdx = 0;
        break;
        case 2:
          millisA2Press = 0;
          if (--hIdx > 3) hIdx = 3; // Da es ein Byte ist
        break;
        case 3:
          newPanel = 7;

          Firmata.setPinState(7, hIdx & 1);
          Firmata.setPinState(8, (hIdx & 2) == 2);
          Firmata.setPinState(10, HIGH);
        break;
      }

    break;

    case 5:

      if (SETUP_CUR_PANEL != CUR_PANEL) {
        myDisplay.displayClear();
        myDisplay.setTextAlignment(PA_CENTER);
        Firmata.setPinState(11, LOW);
      }

      myDisplay.print(promTypeArr[SmallUniversalVal]);

      if (tinput > 0) {
        newPanel = 2;
        Firmata.setPinState(11, HIGH);
      }
    break;

    case 6:
      if (SETUP_CUR_PANEL != CUR_PANEL) {
        
        myDisplay.displayClear();
        myDisplay.displayText(gameResultArr[SmallUniversalVal], PA_CENTER, 100, 0, PA_SCROLL_LEFT, PA_SCROLL_LEFT);

      }

    break;

    case 7:
      if (SETUP_CUR_PANEL != CUR_PANEL)  {
        hIdx = 0;
        myDisplay.displayClear();
        myDisplay.setTextAlignment(PA_CENTER);
      }

      if (millis() - animMillis > 80) {
        animMillis = millis();
        if (++hIdx > 3) hIdx = 0;
      }

      myDisplay.print(waitingAnim[hIdx]);

    break;
  }

  if (newPanel == 100) SETUP_CUR_PANEL = CUR_PANEL;
  else {
    millisA2Press = millisA1Press = 0;
    CUR_PANEL = newPanel;
    Firmata.setPinState(4, LOW);
    Firmata.setPinState(5, LOW);
    Firmata.setPinState(9, anyinputthispanel = LOW);
  }
}
