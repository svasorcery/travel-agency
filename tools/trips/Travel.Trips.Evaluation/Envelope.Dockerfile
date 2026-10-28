FROM mcr.microsoft.com/dotnet/aspnet@sha256:0fa044f682cb7d93a5a90401a00c626c66f7b00b86922be9441f869eae039f80 AS runtime
FROM pgvector/pgvector@sha256:7ae6051efd0e60444282c27c7e141af07f322ce033300e727a49c3dd11075e38
COPY --from=runtime /usr/share/dotnet /usr/share/dotnet
COPY envelope.sh /usr/local/bin/trips-envelope.sh
RUN mkdir -p /app /evidence && touch /evidence/current.journal && chmod 755 /usr/local/bin/trips-envelope.sh
ENV PATH="/usr/share/dotnet:${PATH}" DOTNET_EnableDiagnostics=0 COMPlus_DbgEnableMiniDump=0 DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /app
USER 999:999
ENTRYPOINT ["/usr/bin/timeout","--signal=TERM","--kill-after=5s","21600","/usr/local/bin/trips-envelope.sh"]
