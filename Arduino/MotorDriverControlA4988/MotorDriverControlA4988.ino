#define stepPin 2
#define dirPin 5 
#define testLEDPin 8 

const int m_rpmMax = 200;
 
void setup() {
  pinMode(stepPin, OUTPUT); 
  pinMode(dirPin, OUTPUT);
  pinMode(testLEDPin, OUTPUT);

  Serial.begin(9600);
}

int Clamp(int pVal, int pMin, int pMax) {
  if (pVal < pMin) return pMin;
  else if (pVal > pMax) return pMax;
  return pVal;
}

void TURN(int pSteps, int pRPM, bool pClockwise) {
  pRPM = Clamp(pRPM, 1, m_rpmMax);
  int microSecondDelay = (int)(30000000 / ((double)pRPM * 200.0)); //30 Mio, da es 60 Mio Mikro-Sek pro Minute gibt und zweimal das delay eingeschalten wird 30 Mio * 2 = 60 Mio

  digitalWrite(dirPin, !pClockwise);
  for(int c = 0; c < pSteps; c++) {
    digitalWrite(stepPin, HIGH); 
    delayMicroseconds(microSecondDelay);
    digitalWrite(stepPin, LOW); 
    delayMicroseconds(microSecondDelay); 
  }
}

void loop() {
  TURN(300, 100, true);
  delay(100);
  TURN(300, 50, false);
  delay(100);
}