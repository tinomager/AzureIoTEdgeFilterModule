namespace AzureIoTEdgeModuleShared {
    
    public class ScoredDHTMessageBody : DHTMessageBody{
        public bool IsAnomaly{ get; set; }

        public ScoredDHTMessageBody(){}

        public ScoredDHTMessageBody(DHTMessageBody baseMessage)
        {
            this.temperature = baseMessage.temperature;
            this.humidity = baseMessage.humidity;
            this.timeCreated = baseMessage.timeCreated;
        }

        public ScoredDHTMessageBody(DHTMessageBody baseMessage, bool isAnomaly) : this(baseMessage)
        {
            this.IsAnomaly = isAnomaly;
        }
    }
}