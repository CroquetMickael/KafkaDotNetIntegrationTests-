Feature: Kafka Consumer Integration
  As a developer
  I want to test the integration of MeteoConsumerService
  So that I can verify it consumes messages from Kafka

  Scenario: Consume a message from Kafka
    Given The kafka topic meteo with a message "Test Message"
    When the MeteoConsumerService starts
    Then The message "Test Message" should be consumed