using System;
using System.Collections.Generic;

namespace Closebot
{
    class DataSet
    {
        public long id { get; set; }
        public string dataset_code { get; set; }
        public string database_code { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public DateTime refreshed_at { get; set; }
        public string newest_available_date { get; set; }
        public string oldest_available_date { get; set; }
        public List<string> column_names { get; set; }
        public string frequency { get; set; }
        public string type { get; set; }
        public string premium { get; set; }
        public string limit { get; set; }
        public string transform { get; set; }
        public string column_index { get; set; }
        public string start_date { get; set; }
        public string end_date { get; set; }
        public List<List<object>> data { get; set; }
        public string collapse { get; set; }
        public string order { get; set; }
        public long database_id { get; set; }
    }
}
