namespace AzureIoTEdgeFilterModule{

    public class MachineInteractionCommand{
        public double Temperature { get; set; } 
        public double Humidity { get; set; } 

        public InteractionCommandLevel CommandLevel { get; set;}
    }

    public enum InteractionCommandLevel{
        Debug = 0,
        Info = 1,

        Warning = 2,

        Critical = 3
    }
}