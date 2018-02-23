# AzureIoTEdgeFilterModule  
Azure IoT Edge module for filtering scored DHT data  
  
##Supported inputs and outputs  
The module supports one input "input1" for receiving scored DHT data.  
Data is scored within the anomaly detection module and relevant data for upstream is provided via "output1".  
  
##Supported desired properties
TemperatureThresholdUpper -> Upper threshold on temperature for critical events  
TemperatureThresholdLower -> Lower threshold on temperature for critical events  
HumidityThresholdUpper -> Upper threshold on humidity for critical events  
HumidityThresholdLower -> Lower threshold on humidity for critical events  
WebServerUrl -> Url for notifications on local machine when something relevant is detected