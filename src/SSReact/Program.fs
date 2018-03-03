module SSReact.App

open FSharp.Control.Tasks.TaskBuilder

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Http
open Giraffe
open Thot.Json.Net
open System

module JsonExtraDecode =
    let decodeNonEmptyString token =
        Decode.string token
        |> (fun res ->
            match res with
            | Ok s when (String.IsNullOrEmpty(s) |> not) -> Result.Ok s
            | Ok _ -> Decode.DecoderError.BadPrimitive ("non empty string", token) |> Error
            | Error e -> Error e
        )
// ---------------------------------
// Models
// ---------------------------------
module Dto =
    type Message = { Text: string }

    type Person = 
        { 
            Name:string
            Age:int
        }
        
        static member Decoder =
            Decode.decode
                (fun age name ->
                    { Age = age
                      Name = name
                      })
                |> Decode.required "age" Decode.int
                |> Decode.required "name" JsonExtraDecode.decodeNonEmptyString
        
        static member Encoder (person : Person) =
            Encode.object
                [ 
                    "age", Encode.int person.Age
                    "name", Encode.string person.Name
                ]

// ---------------------------------
// Views
// ---------------------------------

module Views =
    open Fable.Helpers.React
    open Fable.Helpers.React.Props
    open Fable.Import.React
//    open Fulma
//    open Fulma.Elements
//    open Fulma.Layouts

    let layout (content:ReactElement list) =
        html [] [
            head [] [
                title []  [ str "ServerSide React as ViewEngine + Thot.Json.Net" ]
                link [ Rel  "stylesheet"
                       Type "text/css"
                       Href "https://cdnjs.cloudflare.com/ajax/libs/bulma/0.6.2/css/bulma.min.css" ]
                link [ Rel  "stylesheet"
                       Type "text/css"
                       Href "main.css" ]
            ]
            body [] [
                main [ClassName "container"] content
            ]
        ]

    let partial () =
        h1 [ClassName "title"] [ str "ServerSide React as ViewEngine + Thot.Json.Net" ]

    let index (model : Dto.Message) =
        [
            partial()
            p [Id "msg"; ClassName "subtitle"] [ str model.Text ]
            p [Id "error"; ClassName "has-text-danger"] []
            form [Action "/person"] [
                div [ClassName "field"] [
                    label [ClassName "label"] [ str "Name"]
                    div [] [
                        input [ClassName "input"; Type "text"; Placeholder "Name"; Name "name" ]
                    ]
                ]
                div [ClassName "field"] [
                    label [ClassName "label"] [ str "Age"]
                    div [] [
                        input [ClassName "input"; Type "number"; Placeholder "Age"; Name "age"]
                    ]
                ]
                input [Type "submit"; ClassName "button is-primary"; Value "Submit"]
            ]
            script [] [
                """
                const $ = selector => document.querySelector(selector);
                function getValue(name){
                    return $(`input[name=${name}]`).value;
                }
                
                function post(ev) {
                    ev.preventDefault();
                    const action = this.getAttribute("action");
                    const data = JSON.stringify(
                        {
                            "name":getValue("name"), 
                            "age":parseInt(getValue("age"))
                        }
                    );
                    fetch(action, { body: data, method:'POST'})
                        .then(res => {
                            if(res.status === 400) {
                                res.text().then(txt => $("#error").textContent = txt);
                            }
                            else {
                                res.json().then(json => { $("#msg").textContent = json.name; });
                            }
                        });
                }
                window.addEventListener("load", () => { $("form").addEventListener("submit", post); });
                """
                |> RawText
            ]
        ] |> layout



// ---------------------------------
// Web app
// ---------------------------------

let indexHandler (name : string) =
    let greetings = sprintf "Hello %s, from Giraffe!" name
    let model:Dto.Message     = { Text = greetings }
    let view      = Views.index model
    let htmlContent = Fable.Helpers.ReactServer.renderToString view
    fun next (ctx:HttpContext) ->
        ctx.WriteHtmlStringAsync(htmlContent)

let postJson next (ctx:HttpContext) =
    task {
        let! json = ctx.ReadBodyFromRequestAsync()
        let result = Decode.decodeString Dto.Person.Decoder json
        let response = 
            match result with
                | Ok person -> 
                    let updatedPerson = { person with Name = sprintf "%s (%i) at %s" person.Name person.Age (DateTime.Now.ToString("T")) }
                    let newJson = Encode.encode 4 (Dto.Person.Encoder(updatedPerson))
                    Successful.ok (setBodyFromString newJson >=> setHttpHeader "Content-Type" "application/json")
                | Error err ->
                    RequestErrors.BAD_REQUEST err
        return! response next ctx
    }

let webApp =
    choose [
        GET >=>
            choose [
                route "/" >=> indexHandler "world"
                routef "/hello/%s" indexHandler
            ]
        POST >=> route "/person" >=> postJson
        setStatusCode 404 >=> text "Not Found" ]

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(EventId(), ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message


let configureApp (app : IApplicationBuilder) =
    let env = app.ApplicationServices.GetService<IHostingEnvironment>()
    (match env.IsDevelopment() with
    | true  -> app.UseDeveloperExceptionPage()
    | false -> app.UseGiraffeErrorHandler errorHandler)
        .UseStaticFiles()
        .UseGiraffe(webApp)

let configureServices (services : IServiceCollection) =
    services.AddGiraffe() |> ignore

[<EntryPoint>]
let main _ =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    WebHostBuilder()
        .UseKestrel()
        .UseContentRoot(contentRoot)
        .UseIISIntegration()
        .UseWebRoot(webRoot)
        .Configure(System.Action<IApplicationBuilder>(configureApp))
        .ConfigureServices(configureServices)
        .Build()
        .Run()
    0