module ZipfGen

open System

let generateList n =
    let rand = Random()
    let high = n
    let low = high-int (ceil (float high)/10.0)
    let sd = if n < 50 then 3 else 10
    let mutable i = 2
    let randList = List.init n (fun x -> 
        let toRet = rand.Next(max (low/(x+1)) 0, max (high/(x+1)) sd)

        if rand.Next(2)=1 then
            min (n-1) (toRet + rand.Next(sd))
        else
            max (rand.Next(sd)) (toRet - rand.Next(sd))
    )
    randList
