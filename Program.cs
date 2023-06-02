using Azure.Communication;
using Azure.Communication.CallAutomation;
using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Azure.Messaging.EventGrid.SystemEvents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json.Nodes;
using CallAutomation.Contracts;
using Microsoft.Extensions.Logging;
using System.Reflection.Metadata.Ecma335;
using System.Net;
using System.Text;
using static System.Net.WebRequestMethods;
using CallAutomationTest;
using System.Runtime.InteropServices.JavaScript;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var client = new CallAutomationClient(builder.Configuration["ConnectionString"]);
var baseUri = Environment.GetEnvironmentVariable("VS_TUNNEL_URL")?.TrimEnd('/');
if (string.IsNullOrEmpty(baseUri))
{
    baseUri = builder.Configuration["BaseUri"];
}

var app = builder.Build();

app.MapPost("/api/incomingCall", async (
    [FromBody] EventGridEvent[] eventGridEvents,
    ILogger<Program> logger) =>
{
    foreach (var eventGridEvent in eventGridEvents)
    {
        logger.LogInformation($"Incoming Call event received : {JsonConvert.SerializeObject(eventGridEvent)}");
        // Handle system events
        if (eventGridEvent.TryGetSystemEventData(out object eventData))
        {
            // Handle the subscription validation event.
            if (eventData is SubscriptionValidationEventData subscriptionValidationEventData)
            {
                var responseData = new SubscriptionValidationResponse
                {
                    ValidationResponse = subscriptionValidationEventData.ValidationCode
                };
                return Results.Ok(responseData);
            }
        }
        var jsonObject = JsonNode.Parse(eventGridEvent.Data).AsObject();
        var callerId = (string)(jsonObject["from"]["rawId"]);
        var incomingCallContext = (string)jsonObject["incomingCallContext"];
        var callbackUri = new Uri(baseUri + $"/api/calls/{Guid.NewGuid()}?callerId={callerId}");

        AnswerCallResult answerCallResult = await client.AnswerCallAsync(incomingCallContext, callbackUri);
        
    }
    return Results.Ok();
});

app.MapGet("/resume", () =>
{
    Debug.WriteLine("resume endpoint");
    //client.GetCallRecording().ResumeRecording(recID);
    return Results.Ok();
});

app.MapPost("/api/calls/{contextId}", async (
    [FromBody] CloudEvent[] cloudEvents,
    [FromRoute] string contextId,
    [Required] string callerId,
    ILogger<Program> logger) =>
{
    var audioPlayOptions = new PlayOptions() { OperationContext = "SimpleIVR", Loop = false };

    foreach (var cloudEvent in cloudEvents)
    {
        CallAutomationEventBase @event = CallAutomationEventParser.Parse(cloudEvent);
        logger.LogInformation($"Event received: {JsonConvert.SerializeObject(@event)}");

        //StartRecordingOptions recordingOptions = new StartRecordingOptions(new ServerCallLocator("<serverCallId>"))
        //            .setRecordingChannel(RecordingChannel.UNMIXED)
        //            .setRecordingFormat(RecordingFormat.WAV)
        //            .setRecordingContent(RecordingContent.AUDIO)
        //            .setRecordingStateCallbackUrl("<recordingStateCallbackUrl>");

        var callConnection = client.GetCallConnection(@event.CallConnectionId);
        var callMedia = callConnection?.GetCallMedia();

        if (callConnection == null || callMedia == null)
        {
            return Results.BadRequest($"Call objects failed to get for connection id {@event.CallConnectionId}.");
        }

        if (@event is CallConnected)
        {
            logger.LogInformation($"Call is connected!");
            // Start recognize prompt - play audio and recognize 1-digit DTMF input
            var recognizeOptions =
                new CallMediaRecognizeDtmfOptions(CommunicationIdentifier.FromRawId(callerId), maxTonesToCollect: 1)
                {
                    InterruptPrompt = true,
                    InterToneTimeout = TimeSpan.FromSeconds(10),
                    InitialSilenceTimeout = TimeSpan.FromSeconds(5),
                    Prompt = new FileSource(new Uri(baseUri + builder.Configuration["MainMenuAudio"])),
                    OperationContext = "MainMenu"
                };
            await callMedia.StartRecognizingAsync(recognizeOptions);
        }
        if (@event is RecognizeCompleted { OperationContext: "MainMenu" })
        {
            var recognizeCompleted = (RecognizeCompleted)@event;

            if (recognizeCompleted.CollectTonesResult.Tones[0] == DtmfTone.One)
            {
                PlaySource salesAudio = new FileSource(new Uri(baseUri + builder.Configuration["SalesAudio"]));
                await callMedia.PlayToAllAsync(salesAudio, audioPlayOptions);
            }
            else if (recognizeCompleted.CollectTonesResult.Tones[0] == DtmfTone.Two)
            {
                PlaySource marketingAudio = new FileSource(new Uri(baseUri + builder.Configuration["MarketingAudio"]));
                await callMedia.PlayToAllAsync(marketingAudio, audioPlayOptions);
            }
            else if (recognizeCompleted.CollectTonesResult.Tones[0] == DtmfTone.Three)
            {
                PlaySource customerCareAudio = new FileSource(new Uri(baseUri + builder.Configuration["CustomerCareAudio"]));
                await callMedia.PlayToAllAsync(customerCareAudio, audioPlayOptions);
            }
            if (recognizeCompleted.CollectTonesResult.Tones[0] == DtmfTone.Four)
            {
                PlaySource agentAudio = new FileSource(new Uri(baseUri + builder.Configuration["AgentAudio"]));
                audioPlayOptions.OperationContext = "AgentConnect";
                await callMedia.PlayToAllAsync(agentAudio, audioPlayOptions);
                //PlaySource agentAudio = new FileSource(new Uri(baseUri + builder.Configuration["ManualToAddNumberAudio"]));
                //audioPlayOptions.OperationContext = "Manual";
                //await callMedia.PlayToAllAsync(agentAudio, audioPlayOptions);

            }
            //else if (recognizeCompleted.CollectTonesResult.Tones[0] == DtmfTone.Five) //Inprogress, Added videos are not playing correctly
            //{
            //    PlaySource businessCareAudio = new FileSource(new Uri(baseUri + builder.Configuration["BusinessHoursAudio"]));
            //    await callMedia.PlayToAllAsync(businessCareAudio, audioPlayOptions);
            //}
            //else if (recognizeCompleted.CollectTonesResult.Tones[0] == DtmfTone.Six)
            //{
            //    PlaySource agentAudio = new FileSource(new Uri(baseUri + builder.Configuration["TransferToAgentAudio"]));
            //    audioPlayOptions.OperationContext = "Transfer";
            //    await callMedia.PlayToAllAsync(agentAudio, audioPlayOptions);
            //}
            //else if (recognizeCompleted.CollectTonesResult.Tones[0] == DtmfTone.Seven)
            //{
            //PlaySource agentAudio = new FileSource(new Uri(baseUri + builder.Configuration["ManualToAddNumberAudio"]));
            //audioPlayOptions.OperationContext = "Manual";
            //await callMedia.PlayToAllAsync(agentAudio, audioPlayOptions);
            //}
            else if (recognizeCompleted.CollectTonesResult.Tones[0] == DtmfTone.Six)
            {
                // Hangup for everyone
                await callConnection.HangUpAsync(true);
            }
            else
            {
                PlaySource invalidAudio = new FileSource(new Uri(baseUri + builder.Configuration["InvalidAudio"]));
                await callMedia.PlayToAllAsync(invalidAudio, audioPlayOptions);
            }
        }
        if (@event is RecognizeFailed { OperationContext: "MainMenu" })
        {
            // play invalid audio
            await callMedia.PlayToAllAsync(new FileSource(new Uri(baseUri + builder.Configuration["InvalidAudio"])), audioPlayOptions);
        }
        if (@event is PlayCompleted)
        {
            if (@event.OperationContext == "AgentConnect")
            {
                //var addParticipantOptions = new AddParticipantsOptions(new List<CommunicationIdentifier>()
                //{
                //    new PhoneNumberIdentifier(builder.Configuration["ParticipantToAdd"])
                //});

                //addParticipantOptions.SourceCallerId = new PhoneNumberIdentifier(builder.Configuration["ACSAlternatePhoneNumber"]);
                //await callConnection.AddParticipantsAsync(addParticipantOptions);
                var transferParticipantOptions = new TransferToParticipantOptions(new PhoneNumberIdentifier(builder.Configuration["ParticipantToAdd"]));
                transferParticipantOptions.SourceCallerId = new PhoneNumberIdentifier(builder.Configuration["ACSAlternatePhoneNumber"]);
                await callConnection.TransferCallToParticipantAsync(transferParticipantOptions);

            }
            //if (@event.OperationContext == "Manual")
            //{
            //    var transferParticipantOptions = new TransferToParticipantOptions(new PhoneNumberIdentifier(builder.Configuration["ParticipantToAdd"]));

            //    transferParticipantOptions.SourceCallerId = new PhoneNumberIdentifier(builder.Configuration["ACSAlternatePhoneNumber"]);
            //    await callConnection.TransferCallToParticipantAsync(transferParticipantOptions);
            //}
            if (@event.OperationContext == "SimpleIVR")
            {
                await callConnection.HangUpAsync(true);
            }
         
        
        }
        if (@event is PlayFailed)
        {
            logger.LogInformation($"PlayFailed Event: {JsonConvert.SerializeObject(@event)}");
            await callConnection.HangUpAsync(true);
        }
    }
    return Results.Ok();
}).Produces(StatusCodes.Status200OK);


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
           Path.Combine(builder.Environment.ContentRootPath, "audio")),
    RequestPath = "/audio"
});

app.UseAuthorization();

app.MapControllers();

app.Run();
