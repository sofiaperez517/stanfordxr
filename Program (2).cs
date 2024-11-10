using System;
using System.Media;
using System.Collections;
using UnityEngine;
using Amazon;
using Amazon.S3;
using Amazon.Runtime;
using Amazon.TranscribeService;
using Amazon.TranscribeService.Model;
using Amazon.Polly;
using Amazon.Polly.Model;
using Amazon.S3.Transfer;
using Amazon.S3.Model;
using System.Threading.Tasks;
using OpenAI;
using Newtonsoft.Json;
using System.Text;
using System.IO;

public class AWSTranscription : MonoBehaviour
{
    private static string bucketName = "immerse-bay";
    private static string accessKey = "";
    private static string secretKey = "";
    private static string region = "us-east-1";
    private static string localFilePath = "/Users/yashsaxena/Desktop/Spanish audio.mp3";
    private static string gptAccessKey = "";

    private static string audioFileUri = "s3://immerse-bay/Spanish audio.mp3";
    // Change the name later if needed

    private  static string audioFileKey = "Spanish audio.mp3";

    private static string apiEndpoint = "https://api.openai.com/v1/completions";

    private static string scorerRole = "You are a conversational Spanish Language Tutor. In any future messages I send, your only response will be in the form a number that equals 10 times the number of sentences spoken minus 2 times the number of mistakes made.";
    private static string conversationRole = "You are a conversational Spanish Language Learning Buddy. You will speak to me in basic Spanish in order to help me build up my skills in Spanish as I speak to you.";
    private static string outputFilePath = "gptvocaloutput.mp3";


    async void Start()
    {
        var credentials = new BasicAWSCredentials(accessKey, secretKey);
        var s3Config = new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(region) };
        var s3Client = new AmazonS3Client(credentials, s3Config);
        await UploadAudioFile(s3Client, localFilePath);


        var transConfig = new AmazonTranscribeServiceConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(region) };
        var transClient = new AmazonTranscribeServiceClient(credentials, transConfig);
        var transcriptedText = await StartTranscriptionJob(transClient);

        await DeleteAudioFile(s3Client, audioFileKey);

        string gptConvoResponse = await GetCompletionAsync(string.Concat(conversationRole, transcriptedText));
        string gptScorerResponse = await GetCompletionAsync(string.Concat(scorerRole, transcriptedText));

        var pollyConfig = new AmazonPollyConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(region)};
        var pollyClient = new AmazonPollyClient(credentials, pollyConfig);
        string outputedFilePath = "/Users/yashsaxena/Desktop/.mp3";
        await TextToSpeech(pollyClient, gptConvoResponse, outputedFilePath);

    }

    // try using async and await on these based on when you want these events to trigger in unity
    private static async Task UploadAudioFile(AmazonS3Client s3Client, string filePath)
    {
        try
        {
            var transferUtility = new TransferUtility(s3Client);

            string key = Path.GetFileName(filePath);
            transferUtility.Upload(filePath, bucketName, key);

            Debug.Log("File Uploaded to S3" + key);

        }
        catch (AmazonS3Exception e)
        {
            Debug.Log("Error in uploaidng to s3" + e.Message);
        }
        catch (Exception e) 
        {
            Debug.Log("General Error" + e.Message);
        }
    }

    private static async Task<StartTranscriptionJobResponse> StartTranscriptionJob(AmazonTranscribeServiceClient transClient) 
    {
        Guid myuuid = Guid.NewGuid();
        string myuuidAsString = myuuid.ToString();

        var request = new StartTranscriptionJobRequest 
        {
            TranscriptionJobName = myuuidAsString,
            Media = new Media
            {
                MediaFileUri = audioFileUri
                // name of audiofileuri may change in future
            },
            MediaFormat = MediaFormat.Mp3,
            LanguageCode = Amazon.TranscribeService.LanguageCode.EsUS,
            OutputBucketName = bucketName
        };

        try
        {
            var response = await transClient.StartTranscriptionJobAsync(request);
            Debug.Log("Transcription job started successfully: " + response.TranscriptionJob.TranscriptionJobName);
            Debug.Log(response);
            return response;
        }
        catch (AmazonServiceException ex)
        {
            Debug.LogError("Error starting transcription job: " + ex.Message);
            return null;
        }
    }

    private static async Task DeleteAudioFile(AmazonS3Client s3Client, string fileKey)
    {
        try {
            Amazon.S3.Model.DeleteObjectRequest deleteRequest = new Amazon.S3.Model.DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = fileKey
            };

            s3Client.DeleteObjectAsync(deleteRequest);


            Debug.Log("File deleted successfully from S3: ");
        }
        catch (AmazonS3Exception e)
        {
            Debug.LogError("Error deleting file from S3: " + e.Message);
        }
        catch (Exception e)
        {
            Debug.LogError("General error: " + e.Message);
        }
    }


    public static async Task<string> GetCompletionAsync(string prompt)
    {
        using (var client = new HttpClient())
        {
            // Set up the request headers
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {gptAccessKey}");
            client.DefaultRequestHeaders.Add("Content-Type", "application/json");
            // Create the request body
            var requestBody = new
            {
                model = "gpt-4o-mini",  // Change model if needed
                prompt = prompt,
                max_tokens = 100,  // Max tokens (length of the output)
                temperature = 0.7   // Control randomness of the output
            };

            // Serialize the request body to JSON
            var jsonBody = JsonConvert.SerializeObject(requestBody);

            // Create the HTTP content
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                
            try
            {
                // Make the POST request
                var response = await client.PostAsync(apiEndpoint, content);
                    
                // Ensure we get a successful response
                response.EnsureSuccessStatusCode();

                // Read and deserialize the response body
                var responseBody = await response.Content.ReadAsStringAsync();
                var jsonResponse = JsonConvert.DeserializeObject<dynamic>(responseBody);
                return jsonResponse.choices[0].text;
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., network errors, invalid API key)
                Console.WriteLine("Error calling OpenAI API: " + ex.Message);
                return null;
            }
        }
    }

    private static async Task<SynthesizeSpeechRequest> TextToSpeech(AmazonPollyClient pollyClient, string text, string outputFilePath)
    {
        try
        {
            // Create a request to synthesize the speech
            var request = new SynthesizeSpeechRequest
            {
                Text = text,
                OutputFormat = OutputFormat.Mp3,  // You can use MP3, OggVorbis, or other formats
                VoiceId = VoiceId.Mia  // Choose the voice (Joanna is a common option)
            };

            // Call Polly to synthesize the speech
            SynthesizeSpeechResponse response = await pollyClient.SynthesizeSpeechAsync(request);

            // Check if the response contains an audio stream
            if (response.AudioStream != null)
            {
                // Write the audio stream to a file (e.g., output.mp3)
                using (var fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                {
                    response.AudioStream.CopyToAsync(fileStream);
                    Console.WriteLine($"Audio saved successfully to {outputFilePath}");
                }
            }
            return null;
        }
        catch (AmazonPollyException ex)
        {
            Console.WriteLine($"Error synthesizing speech: {ex.Message}");
            return null;
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"General error: {ex.Message}");
            return null;
        }
    }

