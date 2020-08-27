using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Samples.AspNet.HttpClientIssue.WebApi.Models
{
    public class Blog
    {
        public int BlogId { get; set; }
        public string Url { get; set; }

        public List<Post> Posts { get; } = new List<Post>();
    }
}
