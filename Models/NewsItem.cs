using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsSentimentSimple.Models
{   
    record NewsItem(string Title, DateTime PublishedUtc);
}
