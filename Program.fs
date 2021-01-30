module TweeterEngine

open Akka.FSharp
open Akka.Actor
open System.Collections.Generic
open Types
open System.Diagnostics

let tweeterEngine (mailbox:Actor<_>) = 
    let rand = System.Random()
    let users = new List<string>()
    let tweets = new List<Tweet>()
    let subscriptions = new Dictionary<int, HashSet<int>>()
    let hashTagsMap = new Dictionary<string, List<int>>() // Maps hashtags to tweet id.
    let mentionsMap = new Dictionary<int, List<int>>() // Maps mentions to tweet id.
    let loggedInUsers = new HashSet<int>()
    let mutable retweetCount = 0
    let mutable requestsReceived = 0L
    let mutable queryType = 'N'
    let rec loop() = actor {
        let! message = mailbox.Receive()
        requestsReceived <- requestsReceived + 1L
        match message with
            | Register(name) -> 
                users.Add(name)
                let id = users.Count-1
                mailbox.Sender() <! RegistrationSuccess(id)
                subscriptions.Add(id, new HashSet<int>())
                mentionsMap.Add(id, new List<int>())
            | Login(name, id) ->
                if users.Item(id)=name then
                    loggedInUsers.Add(id) |> ignore
            | Logout(id) ->
                loggedInUsers.Remove(id) |> ignore
            | Subscribe(id, nameList) -> 
                let subList = subscriptions.Item(id)
                for name in nameList do
                    subList.Add(users.IndexOf(name)) |> ignore
            | SendTweet(id, content) ->
                let newTweet = { 
                    Id=tweets.Count
                    By=id
                    IsOriginal=true
                    Content=content
                }
                tweets.Add(newTweet)
                let tweetId = newTweet.Id
                let tokens = content.Split(" ")
                for word in tokens do
                    if word.StartsWith("@") then
                        let name = word.Substring(1);
                        let mentionedUserId = users.IndexOf(name)
                        if mentionedUserId >= 0 then
                            let mentions = mentionsMap.Item(mentionedUserId)
                            mentions.Add(tweetId)
                    else if word.StartsWith("#") then 
                        let tag = word.Substring(1)
                        let tweetList = hashTagsMap.GetValueOrDefault(tag, new List<int>())
                        tweetList.Add(tweetId)
                        if not (hashTagsMap.ContainsKey(tag)) then
                            hashTagsMap.Add(tag, tweetList)
            | SendRetweet(id, tweetId) ->
                retweetCount <- retweetCount + 1
                let newRetweet = { 
                    Id=tweets.Count
                    By=id
                    IsOriginal=false
                    Content=if tweets.Item(tweetId).IsOriginal then string tweetId else tweets.Item(tweetId).Content
                }
                tweets.Add(newRetweet)
            | QuerySubscriptions(id, start) ->
                queryType <- 'S'
                let toSend = new List<Tweet>();
                let subs = subscriptions.Item(id)
                for i in start..tweets.Count-1 do
                    let tweet = tweets.Item(i)
                    if subs.Contains(tweet.By) then
                        toSend.Add(if tweet.IsOriginal then tweet else (tweets.Item(int tweet.Content)))
                mailbox.Sender() <! QueryResponses(toSend,queryType)
            | QueryHashtags(tags) ->
                queryType <- 'H'
                let toSend = new HashSet<Tweet>();
                for tag in tags do
                    let query = if tag.StartsWith("#") then tag.Substring(1) else tag
                    for i in hashTagsMap.GetValueOrDefault(query, new List<int>()) do
                        let tweet = tweets.Item(i)
                        toSend.Add(if tweet.IsOriginal then tweet else tweets.Item(int tweet.Content)) |> ignore
                mailbox.Sender() <! QueryResponses(new List<Tweet>(toSend),queryType)
            | QueryMentions(id) ->
                queryType <- 'M'
                let toSend = new List<Tweet>()
                for i in mentionsMap.Item(id) do
                    toSend.Add(tweets.Item(i))
                mailbox.Sender() <! QueryResponses(toSend,queryType)
            | GetStats ->
                let overallStats =  {
                    TotalTweets = tweets.Count
                    UsersCount = users.Count
                    // Subs = subscriptions
                    // Hashtags = hashTagsMap
                    // Mentions = mentionsMap
                    RetweetCount = retweetCount
                    RequestsReceived = requestsReceived
                }                
                printfn "Sending Stats"
                mailbox.Sender() <! OverallStats(overallStats)
                printfn "Sending Stats"
        return! loop()
    }
    loop()



