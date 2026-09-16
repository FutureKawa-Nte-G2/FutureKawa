#include <DHT.h>

#define DHTPIN 2      // Broche DATA du capteur
#define DHTTYPE DHT22 // Remplacer par DHT11 si besoin

DHT dht(DHTPIN, DHTTYPE);

void setup() {
  Serial.begin(9600);
  dht.begin();

  Serial.println("Capteur température / humidité démarré");
}

void loop() {
  float humidite = dht.readHumidity();
  float temperature = dht.readTemperature();

  if (isnan(humidite) || isnan(temperature)) {
    Serial.println("Erreur de lecture du capteur !");
    delay(2000);
    return;
  }

  Serial.print("Température : ");
  Serial.print(temperature);
  Serial.print(" °C | Humidité : ");
  Serial.print(humidite);
  Serial.println(" %");

  delay(2000);
}