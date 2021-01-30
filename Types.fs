module Types

open System.Collections.Generic

type Operations =
    | Register of string
    | Login of string*int
    | Logout of int
    | Subscribe of int*List<string>
    | SendTweet of int*string
    | SendRetweet of int*int
    | QuerySubscriptions of int*int
    | QueryMentions of int
    | QueryHashtags of List<string>
    | GetStats    

type Tweet = {
    Id: int
    By: int
    IsOriginal: bool
    Content: string
}

type EngineStats = {
    TotalTweets: int
    UsersCount: int
    // Subs: Dictionary<int, HashSet<int>>
    // Hashtags: Dictionary<string, List<int>>
    // Mentions: Dictionary<int, List<int>>
    RetweetCount: int
    RequestsReceived: int64
}

type Responses = 
    | OverallStats of EngineStats
    | RegistrationSuccess of int 
    | QueryResponses of List<Tweet> * char
