# Firmware

Firmware do ESP32 de cada estante (PlatformIO + Arduino, PubSubClient, ArduinoJson). Recebe comandos MQTT assinados com HMAC, aciona os relés das fechaduras e publica ack, estado das portas e online/offline.

O projeto PlatformIO é criado na T-0105.

Responsável: agente `firmware-esp32`. Contrato MQTT em [../docs/ARQUITETURA.md](../docs/ARQUITETURA.md).
