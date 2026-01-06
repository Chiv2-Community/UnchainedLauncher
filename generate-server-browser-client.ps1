# delete generated client if it exists
if (Test-Path generated-client) {
    Remove-Item generated-client -Recurse -Force
}

docker run --rm -v "${PWD}:/local" openapitools/openapi-generator-cli generate `
    -i https://raw.githubusercontent.com/Chiv2-Community/C2ServerBrowserBackend/refs/heads/main/assets/chiv2-server-browser-api.yaml `
    -g csharp `
    -o /local/generated-client `
    --additional-properties=packageName=Unchained.ServerBrowser,apiName=ServerBrowserAPI,equatable=true,modelPropertyNaming=PascalCase,nullableReferenceTypes=true,useDateTimeForDate=true,useSourceGeneration=true,caseInsensitiveResponseHeaders=true