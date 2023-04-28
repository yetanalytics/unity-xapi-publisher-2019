using System;
using XAPI;
using XAPI.Metadata;
using System.Threading.Tasks;
using RestSharp;
using System.Collections.Concurrent;
using UnityEngine;
using Util;

namespace LRS
{
    namespace Domain
    {
        public class Publisher
        {
            public Publisher(String LRSUrl, String LRSKey, String LRSSecret)
            {
                // set location on initialize and cache it.
                this.sender = new Sender(LRSUrl, LRSKey, LRSSecret);
                this.locationTask = GetLocation();
            }

            private Agent formAgent() {
                bool hasEmail = PlayerPrefs.HasKey("LRSEmail");
                bool hasAccount = PlayerPrefs.HasKey("LRSAccountId") && PlayerPrefs.HasKey("LRSHomepage");
                if (hasEmail) 
                {
                    return new Agent 
                    {
                        mbox = "mailto:" + PlayerPrefs.GetString("LRSEmail"),
                        name = PlayerPrefs.GetString("LRSUsernameDisplay")
                    };
                }
                else if (hasAccount)
                {
                    return new Agent
                    {
                        account = new AgentAccount
                        {
                            homePage = PlayerPrefs.GetString("LRSHomepage"),
                            name = PlayerPrefs.GetString("LRSAccountId")
                        },
                        name = PlayerPrefs.GetString("LRSUsernameDisplay")
                    };

                }
                else
                {
                    throw new ArgumentException("Invalid user identifiers. Must supply the following in PlayerPrefs: (LRSAccountId + LRSHomepage) OR LRSEmail");
                }
            }

            // setters getters
            private Task<Location> locationTask { set; get; }
            private static readonly ConcurrentDictionary<string, Location> downloadCache = new();
            private static readonly String VERB_URI = "http://adlnet.gov/expapi/verbs/";
            private String verbUri { get { return VERB_URI; } }
            private Sender sender { set; get; }

            // Player Data
            private bool enableUserLocation { get { return PlayerPrefs.HasKey("LRSEnableUserLocation"); } }
            private String email { get { return String.Format("mailto:{0}", PlayerPrefs.GetString("LRSEmail")); } }

            private Agent user { get { return formAgent(); } }
            private String nameDisplay { get { return PlayerPrefs.GetString("LRSUsernameDisplay"); } }
            private String gameId { get { return PlayerPrefs.GetString("LRSGameId"); } }
            private String gameDisplay { get { return PlayerPrefs.GetString("LRSGameDisplay"); } }
            
            // TODO: make this optional
            private String registrationIdentifier { get { return PlayerPrefs.GetString("LRSSessionIdentifier"); } }

            private String formVerbId(String verb)
            {
                return String.Format("{0}{1}", verbUri, verb);
            }

            // getLocation stuff
            private async Task<RestResponse> GetIp()
            {
                var client = new RestClient("http://canhazip.com");
                var request = new RestRequest("");
                return await client.GetAsync(request);

            }

            private async Task<Location> GetLocation()
            {
                var response = await GetIp();
                var ip = response.Content;
                var client = new RestClient("http://ip-api.com");

                // return whatever we get out of the cache if something exists there.
                if (downloadCache.TryGetValue("location", out Location location))
                {
                    return await Task.FromResult(location);
                }

                // otherwise, we go ahead and populate the cache by calling the API
                return await Task.Run(async () =>
                {
                    location = await client.GetJsonAsync<Location>(string.Format("json/{0}", ip));
                    downloadCache.TryAdd("location", location);

                    return location;
                });
            }

            private async Task<Statement<Agent, Activity>> FormBasicStatement(String verbId,
                                                                             String verbDisplay,
                                                                             Agent user,
                                                                             String gameId,
                                                                             String gameDisplay,
                                                                             String registrationIdentifier)
            {
                Extension contextExtension = new Extension() {
                    platformSettingsMetadata = new PlatformSettings() {
                        platform = Application.platform.ToString()
                    }
                };

                Extension objectDefinitionExtension = new Extension() {
                    vrSettingsMetadata = new VRSettings() {
                        // determines what type of VR device the user is using
                        loadedDeviceName = XR.deviceName()

                    },
                    vrSubsystemMetadata = new VRSubsystems() {
                        // determines whether or not VR is being used at all
                        running = XR.isPresent()
                    }
                };

                // location
                if (enableUserLocation) {
                    Location loc = await this.locationTask;
                    contextExtension.location = loc;
                }

                // statement construction
                return new Statement<Agent, Activity>
                {
                    actor = user,
                    verb = new Verb
                    {
                        id = verbId,
                        display = new LanguageMap
                        {
                            enUS = verbDisplay
                        }
                    },
                    objekt = new Activity
                    {
                        id = gameId,
                        definition = new ActivityDefinition
                        {
                            name = new LanguageMap
                            {
                                enUS = gameDisplay
                            },
                            extensions = objectDefinitionExtension
                        }
                    },
                    context = new Context
                    {
                        registration = registrationIdentifier,
                        platform = gameId,
                        extensions = contextExtension
                    }
                };
            }

            public async Task<Statement<Agent, Activity>> StartedStatement(Agent user,
                                                                          String gameId,
                                                                          String gameDisplay,
                                                                          String registrationIdentifier)
            {
                return await FormBasicStatement(formVerbId("initialized"),
                                                "Initialized",
                                                user,
                                                gameId,
                                                gameDisplay,
                                                registrationIdentifier);
            }

            public async Task<Statement<Agent, Activity>> CompletedStatement(Agent user,
                                                                            String gameId,
                                                                            String gameDisplay,
                                                                            String registrationIdentifier)
            {
                return await FormBasicStatement(formVerbId("completed"),
                                                "Completed",
                                                user,
                                                gameId,
                                                gameDisplay,
                                                registrationIdentifier);
            }


            public async void SendStartedStatement()
            {
                var statement = await StartedStatement(user,
                                                       gameId,
                                                       gameDisplay,
                                                       registrationIdentifier);
                var statementStr = statement.Serialize();
                var response = await sender.SendStatement(statementStr);
                Debug.Log(statementStr);
                Debug.Log(response.Content);
                Debug.Log(response.ResponseStatus);
            }

            public async void SendCompletedStatement()
            {
                var statement = await CompletedStatement(user,
                                                         gameId,
                                                         gameDisplay,
                                                         registrationIdentifier);
                var statementStr = statement.Serialize();
                var response = await sender.SendStatement(statementStr);
                Debug.Log(statementStr);
                Debug.Log(response.Content);
                Debug.Log(response.ResponseStatus);

            }

            public async void SendStatement(String verbId,
                                            String verbDisplay)
            {
                var statement = await FormBasicStatement(verbId,
                                                         verbDisplay,
                                                         user,
                                                         gameId,
                                                         gameDisplay,
                                                         registrationIdentifier);
                var statementStr = statement.Serialize();
                var response = await sender.SendStatement(statementStr);
                Debug.Log(statementStr);
                Debug.Log(response.Content);
                Debug.Log(response.ResponseStatus);

            }

            async void SendCustomStatement(String verbId,
                                           String verbDisplay,
                                           Agent user,
                                           String gameId,
                                           String gameDisplay,
                                           String registrationIdentifier)
            {
                var statement = await FormBasicStatement(verbId,
                                                         verbDisplay,
                                                         user,
                                                         gameId,
                                                         gameDisplay,
                                                         registrationIdentifier);
                var statementStr = statement.Serialize();
                var response = await sender.SendStatement(statementStr);
                Debug.Log(statementStr);
                Debug.Log(response.Content);
                Debug.Log(response.ResponseStatus);

            }

            async void SendScratchStatement<TActor, TObjekt>(Statement<TActor, TObjekt> statement) 
                where TActor: IActor
                where TObjekt: IObjekt
            {
                var statementStr = statement.Serialize();
                var response = await sender.SendStatement(statementStr);
            }
        }
    }
}