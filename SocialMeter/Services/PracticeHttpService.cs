using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SocialMeter.Services
{
    public class PracticeHttpService
    {
        private readonly HttpClient _httpClient;

        //Constructor - this runs when the service is created
        public PracticeHttpService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // Lets get a random cat fact - simple, no auth needed
        public async Task<string> GetRandomCatFactAsync()
        {
            // step 1: Whats the url?
            string url = "https://catfact.ninja/fact";

            // step 2: Make the request
            HttpResponseMessage response = await _httpClient.GetAsync(url);

            // step 3: Check if it worked
            if (response.IsSuccessStatusCode)
            {
                // step 4: Read the response body (content)
                string jsonStrong = await response.Content.ReadAsStringAsync();

                // step 5: Parse the JSON to get the fact
                var catFactData = JsonSerializer.Deserialize<CatFactResponse>(jsonStrong);

                return catFactData.fact;
            }

            return "Failed to get cat fact :(";
        }
    }

    public class CatFactResponse
    {
        public string fact { get; set; } = "";
        public int length { get; set; }
    }
}
