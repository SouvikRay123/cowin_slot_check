using System.Collections.Generic;

namespace DemoWorkerService
{
    public class Center
    {
        public string name { get; set; }
        public string address { get; set; }
        public string pincode { get; set; }
        public string from { get; set; }
        public string to { get; set; }
        public string fee_type { get; set; }
        public List<VaccineFees> vaccine_fees { get; set; }
        public List<Session> sessions { get; set; }
    }

    public class VaccineFees
    {
        public string vaccine { get; set; }
        public string fee { get; set; }
    }

    public class Session
    {
        public string date { get; set; }
        public int available_capacity { get; set; }
        public int available_capacity_dose1 { get; set; }
        public int min_age_limit { get; set; }
        public string vaccine { get; set; }
        public List<string> slots { get; set;}        
    }
}