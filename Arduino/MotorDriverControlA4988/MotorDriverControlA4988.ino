#define stepPin 2
#define dirPin 5 

const int m_rpmMax = 200;
 
void setup() {
  pinMode(stepPin,OUTPUT); 
  pinMode(dirPin,OUTPUT);

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
  TURN(200, 50, true);
  delay(2500);
  TURN(400, 100, false);
  delay(2500);
  TURN(800, 300, true);
  delay(2500);
}