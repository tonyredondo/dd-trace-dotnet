using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Samples.AspNetCoreSimpleController.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class HttpController : ControllerBase
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly ILogger<HttpController> _logger;
        private readonly Uri _address;

        public HttpController(ILogger<HttpController> logger, IHttpClientFactory clientFactory)
        {
            _logger = logger;
            _clientFactory = clientFactory;
            _address = new Uri(Startup.MyAddress, "/hello");
        }

        [HttpGet]
        public async Task<string> Get()
        {
            var client = _clientFactory.CreateClient();
            return await client.GetStringAsync(_address);
        }
    }
}
