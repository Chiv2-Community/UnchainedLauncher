# Unchained.ServerBrowser.Api.DefaultApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|--------|--------------|-------------|
| [**ApiPlayfabClientMatchmakePost**](DefaultApi.md#apiplayfabclientmatchmakepost) | **POST** /api/playfab/Client/Matchmake | Matchmake a client to a server |
| [**ApiTbioGetCurrentGamesPost**](DefaultApi.md#apitbiogetcurrentgamespost) | **POST** /api/tbio/GetCurrentGames | Get a list of servers in Chivalry 2 format |
| [**ApiTbioGetMotdPost**](DefaultApi.md#apitbiogetmotdpost) | **POST** /api/tbio/GetMotd | Get the Message of the Day |
| [**ApiV1AdminBanListDelete**](DefaultApi.md#apiv1adminbanlistdelete) | **DELETE** /api/v1/admin/ban-list | Remove IP addresses from the ban list |
| [**ApiV1AdminBanListGet**](DefaultApi.md#apiv1adminbanlistget) | **GET** /api/v1/admin/ban-list | Get the list of banned IP addresses |
| [**ApiV1AdminBanListPut**](DefaultApi.md#apiv1adminbanlistput) | **PUT** /api/v1/admin/ban-list | Add IP addresses to the ban list |
| [**ApiV1AdminVerifiedListDelete**](DefaultApi.md#apiv1adminverifiedlistdelete) | **DELETE** /api/v1/admin/verified-list | Remove IP addresses from the verified list |
| [**ApiV1AdminVerifiedListGet**](DefaultApi.md#apiv1adminverifiedlistget) | **GET** /api/v1/admin/verified-list | Get the list of verified IP addresses |
| [**ApiV1AdminVerifiedListPut**](DefaultApi.md#apiv1adminverifiedlistput) | **PUT** /api/v1/admin/verified-list | Add IP addresses to the verified list |
| [**ApiV1CheckBannedIpGet**](DefaultApi.md#apiv1checkbannedipget) | **GET** /api/v1/check-banned/{ip} | Check if an IP address is banned |
| [**ApiV1ServersGet**](DefaultApi.md#apiv1serversget) | **GET** /api/v1/servers | Get a list of registered game servers. |
| [**ApiV1ServersPost**](DefaultApi.md#apiv1serverspost) | **POST** /api/v1/servers | Register a game server |
| [**ApiV1ServersUniqueIdDelete**](DefaultApi.md#apiv1serversuniqueiddelete) | **DELETE** /api/v1/servers/{unique_id} | Remove the listing for a game server. |
| [**ApiV1ServersUniqueIdHeartbeatPost**](DefaultApi.md#apiv1serversuniqueidheartbeatpost) | **POST** /api/v1/servers/{unique_id}/heartbeat | Send a keepalive signal to stay on the server list |
| [**ApiV1ServersUniqueIdPut**](DefaultApi.md#apiv1serversuniqueidput) | **PUT** /api/v1/servers/{unique_id} | Update the listing for a game server. This does not count as a heartbeat |
| [**ApiV1SwaggerYamlGet**](DefaultApi.md#apiv1swaggeryamlget) | **GET** /api/v1/swagger.yaml | Get the swagger documentation |

<a id="apiplayfabclientmatchmakepost"></a>
# **ApiPlayfabClientMatchmakePost**
> PlayfabMatchmakeResponse ApiPlayfabClientMatchmakePost (ApiPlayfabClientMatchmakePostRequest apiPlayfabClientMatchmakePostRequest = null)

Matchmake a client to a server


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **apiPlayfabClientMatchmakePostRequest** | [**ApiPlayfabClientMatchmakePostRequest**](ApiPlayfabClientMatchmakePostRequest.md) |  | [optional]  |

### Return type

[**PlayfabMatchmakeResponse**](PlayfabMatchmakeResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **400** | No LobbyId provided |  -  |
| **404** | Lobby does not exist |  -  |
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apitbiogetcurrentgamespost"></a>
# **ApiTbioGetCurrentGamesPost**
> TbioServerListResponse ApiTbioGetCurrentGamesPost ()

Get a list of servers in Chivalry 2 format


### Parameters
This endpoint does not need any parameter.
### Return type

[**TbioServerListResponse**](TbioServerListResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apitbiogetmotdpost"></a>
# **ApiTbioGetMotdPost**
> TbioMotdResponse ApiTbioGetMotdPost (MotdRequest motdRequest = null)

Get the Message of the Day


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **motdRequest** | [**MotdRequest**](MotdRequest.md) |  | [optional]  |

### Return type

[**TbioMotdResponse**](TbioMotdResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1adminbanlistdelete"></a>
# **ApiV1AdminBanListDelete**
> BanListResponse ApiV1AdminBanListDelete (string xCHIV2SERVERBROWSERADMINKEY, IpListRequest ipListRequest)

Remove IP addresses from the ban list


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **xCHIV2SERVERBROWSERADMINKEY** | **string** | The admin key |  |
| **ipListRequest** | [**IpListRequest**](IpListRequest.md) |  |  |

### Return type

[**BanListResponse**](BanListResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **403** | Invalid admin key |  -  |
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1adminbanlistget"></a>
# **ApiV1AdminBanListGet**
> BanListResponse ApiV1AdminBanListGet (string xCHIV2SERVERBROWSERADMINKEY)

Get the list of banned IP addresses


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **xCHIV2SERVERBROWSERADMINKEY** | **string** | The admin key |  |

### Return type

[**BanListResponse**](BanListResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **403** | Invalid admin key |  -  |
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1adminbanlistput"></a>
# **ApiV1AdminBanListPut**
> BanListResponse ApiV1AdminBanListPut (string xCHIV2SERVERBROWSERADMINKEY, IpListRequest ipListRequest)

Add IP addresses to the ban list


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **xCHIV2SERVERBROWSERADMINKEY** | **string** | The admin key |  |
| **ipListRequest** | [**IpListRequest**](IpListRequest.md) |  |  |

### Return type

[**BanListResponse**](BanListResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **400** | Invalid IP address |  -  |
| **403** | Invalid admin key |  -  |
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1adminverifiedlistdelete"></a>
# **ApiV1AdminVerifiedListDelete**
> VerifiedListResponse ApiV1AdminVerifiedListDelete (string xCHIV2SERVERBROWSERADMINKEY, IpListRequest ipListRequest)

Remove IP addresses from the verified list


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **xCHIV2SERVERBROWSERADMINKEY** | **string** | The admin key |  |
| **ipListRequest** | [**IpListRequest**](IpListRequest.md) |  |  |

### Return type

[**VerifiedListResponse**](VerifiedListResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **403** | Invalid admin key |  -  |
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1adminverifiedlistget"></a>
# **ApiV1AdminVerifiedListGet**
> VerifiedListResponse ApiV1AdminVerifiedListGet (string xCHIV2SERVERBROWSERADMINKEY)

Get the list of verified IP addresses


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **xCHIV2SERVERBROWSERADMINKEY** | **string** | The admin key |  |

### Return type

[**VerifiedListResponse**](VerifiedListResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **403** | Invalid admin key |  -  |
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1adminverifiedlistput"></a>
# **ApiV1AdminVerifiedListPut**
> VerifiedListResponse ApiV1AdminVerifiedListPut (string xCHIV2SERVERBROWSERADMINKEY, IpListRequest ipListRequest)

Add IP addresses to the verified list


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **xCHIV2SERVERBROWSERADMINKEY** | **string** | The admin key |  |
| **ipListRequest** | [**IpListRequest**](IpListRequest.md) |  |  |

### Return type

[**VerifiedListResponse**](VerifiedListResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **400** | Invalid IP address |  -  |
| **403** | Invalid admin key |  -  |
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1checkbannedipget"></a>
# **ApiV1CheckBannedIpGet**
> BanStatusResponse ApiV1CheckBannedIpGet (string ip)

Check if an IP address is banned


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **ip** | **string** | The IP address to check |  |

### Return type

[**BanStatusResponse**](BanStatusResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1serversget"></a>
# **ApiV1ServersGet**
> ServerListResponse ApiV1ServersGet ()

Get a list of registered game servers.


### Parameters
This endpoint does not need any parameter.
### Return type

[**ServerListResponse**](ServerListResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1serverspost"></a>
# **ApiV1ServersPost**
> RegistrationResponse ApiV1ServersPost (ServerRegistrationRequest serverRegistrationRequest)

Register a game server


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **serverRegistrationRequest** | [**ServerRegistrationRequest**](ServerRegistrationRequest.md) |  |  |

### Return type

[**RegistrationResponse**](RegistrationResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **201** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1serversuniqueiddelete"></a>
# **ApiV1ServersUniqueIdDelete**
> StatusResponse ApiV1ServersUniqueIdDelete (string xCHIV2SERVERBROWSERKEY, string uniqueId)

Remove the listing for a game server.


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **xCHIV2SERVERBROWSERKEY** | **string** | The key provided at registration time |  |
| **uniqueId** | **string** | The unique id of the server being updated |  |

### Return type

[**StatusResponse**](StatusResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **400** | Something in your request is malformed |  -  |
| **404** | Server not registered |  -  |
| **403** | Invalid key provided |  -  |
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1serversuniqueidheartbeatpost"></a>
# **ApiV1ServersUniqueIdHeartbeatPost**
> UpdateResponse ApiV1ServersUniqueIdHeartbeatPost (string xCHIV2SERVERBROWSERKEY, string uniqueId)

Send a keepalive signal to stay on the server list


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **xCHIV2SERVERBROWSERKEY** | **string** | The key provided at registration time |  |
| **uniqueId** | **string** | The unique id of the server being updated |  |

### Return type

[**UpdateResponse**](UpdateResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **400** | Something in your request is malformed |  -  |
| **403** | You did something you&#39;re not allowed to do with the parameters provided. |  -  |
| **404** | Server not registered |  -  |
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1serversuniqueidput"></a>
# **ApiV1ServersUniqueIdPut**
> UpdateResponse ApiV1ServersUniqueIdPut (string xCHIV2SERVERBROWSERKEY, string uniqueId, UpdateRegisteredServer updateRegisteredServer)

Update the listing for a game server. This does not count as a heartbeat


### Parameters

| Name | Type | Description | Notes |
|------|------|-------------|-------|
| **xCHIV2SERVERBROWSERKEY** | **string** | The key provided at registration time |  |
| **uniqueId** | **string** | The unique id of the server being updated |  |
| **updateRegisteredServer** | [**UpdateRegisteredServer**](UpdateRegisteredServer.md) |  |  |

### Return type

[**UpdateResponse**](UpdateResponse.md)

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: application/json
 - **Accept**: application/json


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **400** | Something in your request is malformed |  -  |
| **403** | You did something you&#39;re not allowed to do with the parameters provided. |  -  |
| **404** | Server not registered |  -  |
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

<a id="apiv1swaggeryamlget"></a>
# **ApiV1SwaggerYamlGet**
> string ApiV1SwaggerYamlGet ()

Get the swagger documentation


### Parameters
This endpoint does not need any parameter.
### Return type

**string**

### Authorization

No authorization required

### HTTP request headers

 - **Content-Type**: Not defined
 - **Accept**: text/yaml


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | Successful operation |  -  |

[[Back to top]](#) [[Back to API list]](../../README.md#documentation-for-api-endpoints) [[Back to Model list]](../../README.md#documentation-for-models) [[Back to README]](../../README.md)

