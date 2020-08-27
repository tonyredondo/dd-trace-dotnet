using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace Samples.AspNet.HttpClientIssue.WebApi.Controllers
{
    public class ValuesController : ApiController
    {
        private void MakeHttpClientCall()
        {
            const string url = "http://www.contoso.com/";

            var client = new HttpClient();
            var response = client.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
        }

        // GET api/values
        public IEnumerable<string> Get()
        {
            MakeHttpClientCall();
            return new string[] { "value1", "value2" };
        }

        // GET api/values/5
        public string Get(int id)
        {
            MakeHttpClientCall();
            return "value";
        }

        // POST api/values
        public void Post([FromBody] string value)
        {
        }

        // PUT api/values/5
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/values/5
        public void Delete(int id)
        {
        }
    }
}
