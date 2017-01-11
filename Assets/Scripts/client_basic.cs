using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
// TODO: Rewirte the code without using 'Tasks', 'async', 'await' and etc.
using System.Threading.Tasks;
// TODO: Copy the following libraries into the Asset folder and see whether it resolves the issue.
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Script.Serialization;
using Newtonsoft.Json;
using System.Threading;
using uPLibrary.Networking.M2Mqtt.Messages;
using uPLibrary.Networking.M2Mqtt;
using MsgPack;
using MsgPack.Serialization;
using System.IO;
using UnityEngine;

namespace NeuroScaleClient
{

  public static class HttpClientExtensions
  {
    public async static Task<HttpResponseMessage> PatchAsync(this HttpClient client, string requestUri, HttpContent content)
    {
      var method = new HttpMethod("PATCH");

      var request = new HttpRequestMessage(method, requestUri)
      {
        Content = content
      };
      return await client.SendAsync(request);
    }
  }
  class NeuroScaleClient
  {
    static MqttClient mqttClientReader;
    static MqttClient mqttClientWriter;
    static Uri read_endpoint;
    static Uri write_endpoint;
    static string readerTopicPath;
    static bool json_fallback = false;

   private NeuroScaleClient()
    {
            // First we need to define the Neuroscale API URL to use; if use are enrolled in
            // a beta program you may have received a custom endpoint instead of the
            // default provided below.
            string api_url = @"https://api.neuroscale.io/";

            // you also need to provide your own access token; for security
            // reasons it is best to keep this string out of your code commits and instead
            // read it from an environment variable or file (if you do not have a token,
            // check our online documentation on how to generate one from your
            // username/password combination)
            string access_token = "c618b253-34bb-40a9-8fc7-1174838d4d24";
            // next we need to identify the pipeline that we want to launch on Neuroscale
            // in this example we will launch the "Echo" pipeline; that pipeline will parse
            // your data but send it back unchanged for testing
            string pipeline_name = "Echo";
            // optionally the instance id if you already have an instance running
            string instance_id = "";

            // next we will look up the pipeline of interest by name from the list
            // of available pipelines
            HttpClient client = new HttpClient();
      client.BaseAddress = new Uri(api_url);
      // construct the HTTP header that will be used for authorization
      client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access_token);
      // Add an Accept header for JSON format.
      client.DefaultRequestHeaders.Accept.Add(
      new MediaTypeWithQualityHeaderValue(@"application/json"));

      // Get data response.
      HttpResponseMessage response = client.GetAsync(@"v1/pipelines").Result;  // Blocking call!
      string pipeline_id = string.Empty;
      if (response.IsSuccessStatusCode)
      {
        // Parse the response body. Blocking!
        string jsonContent = response.Content.ReadAsStringAsync().Result;

        var jsSer = new JavaScriptSerializer();
        var body = jsSer.Deserialize<Dictionary<string, dynamic>>(jsonContent);
        var pipelines = body["data"];
        // now go through all pipelines and pick the one that has a matching name
        foreach (var p in pipelines)
        {
          if (p["name"] == pipeline_name)
          {
            pipeline_id = p["id"];
          }
        }
      }
      else
      {
        Debug.Log((int)response.StatusCode + " ("+ response.ReasonPhrase + ") \nERROR: Could not query available pipelines ");
      }
      // next we will describe the data that we are going to send to the pipeline
      var channel_labels = new List<string>() { "ch1", "ch2", "ch3", "ch4", "ch5", "ch6", "ch7", "ch8" };
      // the sampling rate that we're giving here is the "nominal" sampling rate, but
      // since all our data that we stream up is going to be time-stamped, the cloud
      // system will be able to figure out the actual ("effective") sampling rate,
      // which might be e.g., 99.8 Hz
      int sampling_rate = 100;
      // for well-known modality names, we follow the naming conventions at
      // https://github.com/sccn/xdf/wiki/Meta-Data
      string modality = "EEG";

      // from these parameters we construct the pipeline meta-data, which are part
      // of the required launch options; we need to declare the list of streams that
      // the pipeline is expected to receive and to send back; here we want to send
      // and receive both an EEG stream and a marker stream (see README.md for more
      // info on this syntax); some pipelines can also accept additional signal/marker
      // streams

      // we start making list of channels first.
      var listOfChannels = 
        from d in channel_labels
        select new Dictionary<string, string>() { { "label", d } };

      var eeg_stream = new Dictionary<string, object>() 
      {
        {"name", "myeeg"}, 
        {"type", modality}, 
        {"sampling_rate", sampling_rate},
        {"channels", listOfChannels}
      };

      // if you are not marking time points in the data then you can omit the marker
      // stream; a marker stream must always have type Markers and sampling rate 0,
      // and will usually have a single channel (its label is arbitrary)
      var marker_stream = new Dictionary<string, object>() 
      {
        {"name","mymarkers"},
        {"type", "Markers"},
        {"sampling_rate", 0},
        {"channels", new List<object> ()
                       { 
                         new Dictionary<string, string> ()
                         {
                                  {"label", "mylabel"}
                         }
                       }
        }
      };
      // the list of streams being transmitted
      var streams = new List<object>()
      {
        eeg_stream, 
        marker_stream
      };

      // pipelines communicate with their environment through i/o nodes, and to fully
      // specify the desired i/o behavior of the pipeline, we need to define those
      // nodes and their streams; in our case, the pipeline has only a single node on
      // the input and one on the output, and both of them are usually named "default"
      var node_decl = new Dictionary<string, object>() 
      {
        {"name", "default"}, 
        {"streams", streams}
      };
      // the complete meta-data is now just the list of input and ouput nodes
      var metadata = new Dictionary<string, object> 
      {
        {"nodes", new Dictionary<string, object> () 
                     {
                       {"in",  new List<object>() {node_decl}}, 
                       {"out", new List<object>() {node_decl}}
                     }
        }
    
     };
      // this sample can send either JSON-encoded or msgpack-encoded messages; the
      // msgpack format is recommended for anything other than wire debugging
      json_fallback = false; // true is JSON-encoded

      // assemble the final launch options for the pipeline; for further options have
      // a look at the API docs (for instance, you can toggle some server-side
      // features)
      var parameters = new Dictionary<string, object> 
      {
        {"pipeline", pipeline_id}, 
        {"metadata", metadata},
        {"encoding", json_fallback ? "json": "msgpack"}
      };

      string jsonDumps = JsonConvert.SerializeObject(parameters);
      var content = new StringContent(jsonDumps, Encoding.UTF8, @"application/json");
      HttpResponseMessage postOrPatchResult;
      if (String.IsNullOrWhiteSpace(instance_id))
      {
        // to launch our processing pipeline now we need to create a new "instance"
        // of the desired pipeline (a running piece of code)
        // IMPORTANT: note that, once the instance has been created, you are
        // consuming resources in the cloud until you destroy the instance again
       
        postOrPatchResult = client.PostAsync(@"v1/instances", content).Result;  // Blocking call!
      }
      else
      {
        // we already know the id of a running instance and we'd like to reconfigure
        // it with new parameters -- this is usually much faster
        postOrPatchResult = client.PatchAsync(@"/v1/instances/" + instance_id, content).Result; // Blocking call!
      }

      if (postOrPatchResult.StatusCode == System.Net.HttpStatusCode.Created || 
          postOrPatchResult.StatusCode == System.Net.HttpStatusCode.OK)
      {
        // Parse the response body. Blocking!
        string jsonContent = postOrPatchResult.Content.ReadAsStringAsync().Result;

        var jsSer = new JavaScriptSerializer();
        var postOrPatchBody = jsSer.Deserialize<Dictionary<string, dynamic>>(jsonContent);
        instance_id = postOrPatchBody["id"];
        try
        {
          Debug.Log("instance  "+ instance_id + "  has been requested successfully");
          // wait until the instance has launched; this is generally useful since
          // any data you stream up while the instance is not yet running is lost
          Debug.Log("waiting for instance to come up...");
          var last_state = string.Empty;
          while (last_state != "running")
          {
            HttpResponseMessage comeUpResponse = client.GetAsync(@"/v1/instances/" + instance_id).Result;  // Blocking call!
            jsonContent = comeUpResponse.Content.ReadAsStringAsync().Result;
            jsSer = new JavaScriptSerializer();
            var comeUpResponseBody = jsSer.Deserialize<Dictionary<string, dynamic>>(jsonContent);
            var state = comeUpResponseBody["state"];
            if (state != last_state)
            {
              Debug.Log(state + "...");
              last_state = state;
              Thread.Sleep(1000);
            }
            
          }
          // now that we have an instance, we are ready to stream raw data up and
          // processed data back down

          // for this, we need to extract the read/write endpoints that are
          // provided by the instance and described in the response body

          read_endpoint  = get_endpoint(postOrPatchBody,"read");
          write_endpoint = get_endpoint(postOrPatchBody, "write");

           // as we are using the paho MQTT library we need to declare a few
           // callback functions handle subscription and message receiving; if you
           // are only sending data and not receiving you can omit these as they
           // only provide diagnostics

           // callback functions are defined at the end.

          // having defined the callbacks, we're ready to subscribe to the read
          // endpoint; if you make an application that only uploads data you can
          // skip this part. (the userdata parameter is only used by our own
          // callbacks and is not otherwise necessary; same applies to the
          // subsequent writer setup call)
          MqttSslProtocols sslprotocol = MqttSslProtocols.None;
          System.Security.Cryptography.X509Certificates.X509Certificate caCert = new System.Security.Cryptography.X509Certificates.X509Certificate();
    
     
           mqttClientReader = new MqttClient(read_endpoint.Host, read_endpoint.Port, 
            false, caCert, sslprotocol); 
        
          mqttClientReader.MqttMsgPublishReceived += onReceived;
          mqttClientReader.MqttMsgPublished += onPublish;
          mqttClientReader.MqttMsgSubscribed += onSubscribed;
          mqttClientReader.MqttMsgUnsubscribed += onUnsubscribed;

          byte readerCode = mqttClientReader.Connect(Guid.NewGuid().ToString());
          if (readerCode == 0)
          {
            Debug.Log("Reader connected successfully");
          }
          else
          {
            Debug.Log("Reader failed to reconnected");
            return;
          }
          readerTopicPath = read_endpoint.AbsolutePath + @"/#";
          ushort readerMsgId = mqttClientReader.Subscribe(new string[] { readerTopicPath },
                                          new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });
          // we also hook up the write endpoint; again, if you are not streaming up
          // data you can omit this part
          mqttClientWriter = new MqttClient(write_endpoint.Host, write_endpoint.Port,
           false, caCert, sslprotocol);
          mqttClientWriter.MqttMsgPublishReceived += onReceived;
          mqttClientWriter.MqttMsgPublished += onPublish;
          mqttClientWriter.MqttMsgSubscribed += onSubscribed;
          mqttClientWriter.MqttMsgUnsubscribed += onUnsubscribed;
          mqttClientWriter.ConnectionClosed += onConnectionClosed;
          
          byte writerCode = mqttClientWriter.Connect(Guid.NewGuid().ToString());
          if (writerCode == 0)
          {
            Debug.Log("Writer connected successfully");
          }
          else
          {
            Debug.Log("Writer failed to connected");
            return;
          }
          string writerTopicPath = write_endpoint.AbsolutePath;
          // in the following infinite loop, we're sending and receiving data
          Debug.Log("now sending data...");

          while (true)
          {
            // generate a chunk of 10 random samples of 8 channels
            // (each sample being a list of values, one per channel)
            // typically this would be data you have obtained from an external
            // device.
            // note that using shorter chunks will usually lead to more
            // messages per second being transmitted; there is an upper limit
            // to how many messages per second the backend will pick up,
            // and if you find that the output starts to lag behind by several
            // seconds, most likely your messages are too short
            int chunk_samples = 10;
            // 10 packets of 8 data points (we have 8 channels).
            var samples = new List<List<double>>() {};
            for (int i = 0; i < chunk_samples; i++)
            {
              var octet = new List<double>();
              for (int j = 0; j < channel_labels.Count; j++)
              {
                octet.Add(new System.Random().NextDouble());
              }
              samples.Add(octet);
            }

            // also generate time stamps; since the latest sample was obtained
            // just now, that's what its timestamp should be; the preceding
            // samples' timestamps are successively smaller in accordance with
            // the sampling rate (do not worry about jitter as this is no
            // problem for Neuroscale)
            var now = DateTime.Now.Second;
            var timestamps = (from t in Enumerable.Range(1, chunk_samples).Reverse()
                             select now - (double)t / sampling_rate).ToList(); // Added ToList() to prevent 
            // msgpack error because appraratly msgpack is unable to serialze IEnumerable<T> type.

            // this is all we need to build the EEG chunk that we will submit;
            // note that the name must match one of the names declared earlier
            // in the meta-data
            var eeg_chunk = new Dictionary<string, object>() 
            {
              {"name", "myeeg"},
              {"samples", samples},
              {"timestamps", timestamps}
            };

            // next we want to build the marker chunk that contains a few random
            // markers at some arbitrary time points; ideally all data in a
            // single message that you send up should cover the same time range
            // (if you send only one stream, there is nothing to worry about)
            var markers = new List<List<string>>()
            {
              new List<string>() {"mrk1"},
              new List<string>() {"mrk2"},
              new List<string>() {"mrk3"}
            };

            var marker_stamps = new List<double>() 
            { 
              now - 0.025, 
              now - 0.01, 
              now - 0.05 
            };

            var marker_chunk = new Dictionary<string, object>() 
            {
              {"name", "mymarkers"},
              {"samples", markers},
              {"timestamps", marker_stamps}
            };

            // now build the message that holds the chunk; generally, a message
            // holds a list of one or more stream chunks in a field named
            // streams
            var msg = new Dictionary<string, List<Dictionary<string, object>>>() 
            {
              {"streams",  new List<Dictionary<string, object>> () 
                           {
                             eeg_chunk, 
                             marker_chunk
                           }
              }
            };
            // depending on the pipeline configuration, we need to encode either
            // via JSON or msgpack
            byte[] encodedMsg;
            if (json_fallback)
            {
              // JSON serialization
              var jsonized = JsonConvert.SerializeObject(msg); //  -debug TODO: get it back
              encodedMsg = Encoding.UTF8.GetBytes(jsonized);
            }
            else
            {
              // msgpack serialization
              var serializer = SerializationContext.Default.GetSerializer<Dictionary<string, 
                List<Dictionary<string,object>>>>();
              using (var byteStream = new MemoryStream())
              {
                // Pack obj to stream.
                Debug.Log("msgpacking...");
                serializer.Pack(byteStream, msg);
                encodedMsg = byteStream.ToArray();
              }
            }
            mqttClientWriter.Publish(writerTopicPath, encodedMsg, MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false);
            Thread.Sleep((int)(1000.0 * chunk_samples / sampling_rate));
          } // End of while loop
        }
        catch (Exception e)
        {
          Debug.Log("ERROR:" + e.Message);
          throw;
        }
        finally
        {
          // we unsubscribe explicitly to shut off the log messages
          // (this is only done so that the subsequent Kill prompt is not drowned
          // out among miscellaneous log messages)
          if (mqttClientReader != null)
          {
            mqttClientReader.Unsubscribe(new string[] { readerTopicPath });
            Thread.Sleep(500);
          }
          // Do not forget to terminate the instance after you are done using it
          // this is simply done using a delete request that involves the
          // corresponding instance; note: if you lost your instance id somehow,
          // or what to check what instances you have running, you can query your
          // instances via the call:
          string kill = string.Empty;
          while (kill != "y" || kill != "n")
          {
            kill = Console.ReadLine();
            kill.Trim();
            if (kill == "y")
            {
              var killResponse = client.DeleteAsync("/v1/instances/" + instance_id).Result;
              if (killResponse.IsSuccessStatusCode)
                Debug.Log("instance " + instance_id + " was deleted successfully");
              else
                Debug.Log("instance "+ instance_id +" was not deleted (HTTP " + killResponse.StatusCode + ")");
            }
          }
        }
      }
      else
      {
         Debug.Log("ERROR: could not bring up instance (HTTP " + postOrPatchResult.StatusCode + ")");
      }
    } // End Of Main Method

    static Uri get_endpoint(Dictionary<string, dynamic> body, string mode = "read")
    {
      var urls = new List<string>();
      foreach (var e in body["endpoints"]["data"])
      {
        
        if (e["mode"] == mode)
          urls.Add(e["url"]);
      }
      Uri uri = new Uri(urls.FirstOrDefault<string>());
      return uri;
    }

    /// <summary>
    /// this callback will be invoked whenever we get a message back;
    /// some pipelines have multiple output nodes; these messages from
    /// these nodes generally come in on sub-topics with the same name as
    /// the respective node; to distinguish messages from non-default
    /// nodes, we may compare the topic to the one that we expect, and
    /// discard irrelevant messages
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"> Contains the message and other info.</param>
    static void onReceived(object sender, MqttMsgPublishEventArgs e)
    {
      // this callback will be invoked whenever we get a message back;
      // some pipelines have multiple output nodes; these messages from
      // these nodes generally come in on sub-topics with the same name as
      // the respective node; to distinguish messages from non-default
      // nodes, we may compare the topic to the one that we expect, and
      // discard irrelevant messages
      
      var topic = e.Topic.Split('/').LastOrDefault();
      if (topic != "default")
        Debug.Log("received message on non-default topic ("+ e.Topic + "); ignoring...");
      // the payload must first be decoded using either JSON or msgpack
      // (depending on pipeline settings)
      dynamic data;
      if (json_fallback)
      {
        var payload = Encoding.UTF8.GetString(e.Message);
        data = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(payload);
        var streams = data["streams"];
        if (streams.Count != 0)
          Debug.Log("received message on topic "+ topic + "");
        else
          Debug.Log("received empty message on topic "+ topic + "");
        foreach (var stream in streams)
        {
          var name = stream["name"];
          var samples = stream["samples"];
          var stamps = stream["timestamps"];
          Debug.Log(" streams "+ name +": "+ stamps.Count + " samples");

        }
      }
      else
      {
        var serializer = SerializationContext.Default.GetSerializer<Dictionary<string, List<Dictionary<string, MessagePackObject>>>>();
        using (var byteStream = new MemoryStream(e.Message))
        {
          
          Debug.Log("msg unpacking...");
          data = serializer.Unpack(byteStream);
          List<Dictionary<string, MessagePackObject>> streams = data["streams"];
          if (streams.Count != 0)
            Debug.Log("received message on topic "+topic);
          else
            Debug.Log("received empty message on topic "+ topic);

          foreach (var s in streams)
          {
            var name = s["name"];
            var samples = s["samples"].AsList();
            var stamps = s["timestamps"].AsList();
            Debug.Log("stream "+name +": "+ stamps.Count + " samples");
          }
        }
     
      }
     
    }

    // these callbacks (onPublish, onSubscribed and onUnsubscribed) are most 
    // added for completeness and for extra diagnostics
    static void onPublish(object sender, MqttMsgPublishedEventArgs e)
    {
      Debug.Log("MessageId = " + e.MessageId + " Published = " + e.IsPublished);
    }

    static void onSubscribed(object sender, MqttMsgSubscribedEventArgs e)
    {
      Debug.Log("Subscribed for id = " + e.MessageId);
    }

    static void onUnsubscribed(object sender, MqttMsgUnsubscribedEventArgs e)
    {
      Debug.Log("Unsubscribed for id = " + e.MessageId);
    }

    private static void onConnectionClosed(object sender, EventArgs e)
    {
      Debug.Log("Connection Closed for id = " + e.ToString());
      MqttClient readerOrWriter = sender as MqttClient;
      if (sender != null)
      {
        byte code = mqttClientReader.Connect(Guid.NewGuid().ToString());
        if (code == 0)
          Debug.Log("Reconnected successfully");
        else
          Debug.Log("Failed to reconnected");
       }
     }
      
    } 
  }

