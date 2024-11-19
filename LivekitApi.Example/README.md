# Run

Configure env variables `LIVEKIT_URL`, `LIVEKIT_API_KEY` and `LIVEKIT_API_SECRET`.
You can edit them in `appsettings.json` file.

```env
LIVEKIT_URL=http://localhost:7880
LIVEKIT_API_KEY=devkey
LIVEKIT_API_SECRET=secretsecretsecretsecretsecretsecretsecret
```

Run the application:

```
dotnet run
```

> You can directly open these links in your browser:
>
> - [http://localhost:6080/livekit/token?user=myname&room=myroom](http://localhost:6080/livekit/token?user=myname&room=myroom) to generate tokens.
> - [http://localhost:6080/livekit/api](http://localhost:6080/livekit/api) to retrieve all active rooms, egress and ingress.
