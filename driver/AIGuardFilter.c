#include <fltKernel.h>

#define AIGUARD_POLICY_VERSION 1
#define AIGUARD_MAX_PROTECTED_PATHS 8
#define AIGUARD_MAX_ALLOWED_APPS 32
#define AIGUARD_PATH_CHARS 520
#define AIGUARD_PORT_NAME L"\\ROOOMTECHAIGuardPort"
#define AIGUARD_POOL_TAG 'PGuA'

PFLT_FILTER gFilterHandle = NULL;
PFLT_PORT gServerPort = NULL;
PFLT_PORT gClientPort = NULL;
ERESOURCE gPolicyLock;

typedef struct _AIGUARD_POLICY_MESSAGE {
    ULONG Version;
    ULONG ProtectedPathCount;
    ULONG AllowedApplicationCount;
    WCHAR ProtectedPaths[AIGUARD_MAX_PROTECTED_PATHS][AIGUARD_PATH_CHARS];
    WCHAR AllowedApplications[AIGUARD_MAX_ALLOWED_APPS][AIGUARD_PATH_CHARS];
} AIGUARD_POLICY_MESSAGE, *PAIGUARD_POLICY_MESSAGE;

AIGUARD_POLICY_MESSAGE gPolicy;
BOOLEAN gPolicyConfigured = FALSE;
static const WCHAR* gProtectedMarker = L"\\AI_Guard_Protected\\";

DRIVER_INITIALIZE DriverEntry;

NTSTATUS AiGuardUnload(_In_ FLT_FILTER_UNLOAD_FLAGS Flags);
FLT_PREOP_CALLBACK_STATUS AiGuardPreCreate(
    _Inout_ PFLT_CALLBACK_DATA Data,
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _Flt_CompletionContext_Outptr_ PVOID* CompletionContext);

NTSTATUS AiGuardConnectNotify(
    _In_ PFLT_PORT ClientPort,
    _In_opt_ PVOID ServerPortCookie,
    _In_reads_bytes_opt_(SizeOfContext) PVOID ConnectionContext,
    _In_ ULONG SizeOfContext,
    _Outptr_result_maybenull_ PVOID* ConnectionCookie);

VOID AiGuardDisconnectNotify(_In_opt_ PVOID ConnectionCookie);

NTSTATUS AiGuardMessageNotify(
    _In_opt_ PVOID PortCookie,
    _In_reads_bytes_opt_(InputBufferLength) PVOID InputBuffer,
    _In_ ULONG InputBufferLength,
    _Out_writes_bytes_to_opt_(OutputBufferLength, *ReturnOutputBufferLength) PVOID OutputBuffer,
    _In_ ULONG OutputBufferLength,
    _Out_ PULONG ReturnOutputBufferLength);

CONST FLT_OPERATION_REGISTRATION Callbacks[] = {
    { IRP_MJ_CREATE, 0, AiGuardPreCreate, NULL },
    { IRP_MJ_OPERATION_END }
};

CONST FLT_REGISTRATION FilterRegistration = {
    sizeof(FLT_REGISTRATION),
    FLT_REGISTRATION_VERSION,
    0,
    NULL,
    Callbacks,
    AiGuardUnload,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL
};

static BOOLEAN AiGuardUnicodeContainsInsensitive(
    _In_ PCUNICODE_STRING Haystack,
    _In_ PCWSTR Needle)
{
    UNICODE_STRING needleString;
    ULONG i;

    RtlInitUnicodeString(&needleString, Needle);
    if (Haystack->Length < needleString.Length)
        return FALSE;

    for (i = 0; i <= (Haystack->Length - needleString.Length) / sizeof(WCHAR); i++) {
        UNICODE_STRING candidate;
        candidate.Buffer = Haystack->Buffer + i;
        candidate.Length = needleString.Length;
        candidate.MaximumLength = needleString.Length;
        if (RtlEqualUnicodeString(&candidate, &needleString, TRUE))
            return TRUE;
    }

    return FALSE;
}

static BOOLEAN AiGuardUnicodeStartsWithInsensitive(
    _In_ PCUNICODE_STRING Value,
    _In_ PCWSTR Prefix)
{
    UNICODE_STRING prefixString;
    UNICODE_STRING candidate;
    USHORT prefixChars;

    if (Prefix == NULL || Prefix[0] == L'\0')
        return FALSE;

    RtlInitUnicodeString(&prefixString, Prefix);
    if (Value->Length < prefixString.Length)
        return FALSE;

    candidate.Buffer = Value->Buffer;
    candidate.Length = prefixString.Length;
    candidate.MaximumLength = prefixString.Length;
    if (!RtlEqualUnicodeString(&candidate, &prefixString, TRUE))
        return FALSE;

    if (Value->Length == prefixString.Length)
        return TRUE;

    prefixChars = prefixString.Length / sizeof(WCHAR);
    return Value->Buffer[prefixChars] == L'\\';
}

static BOOLEAN AiGuardPathIsProtected(_In_ PCUNICODE_STRING FileName)
{
    ULONG i;
    ULONG configuredCount;
    BOOLEAN configured;
    BOOLEAN result = FALSE;

    ExAcquireResourceSharedLite(&gPolicyLock, TRUE);
    configured = gPolicyConfigured;
    configuredCount = gPolicy.ProtectedPathCount;
    if (configured) {
        for (i = 0; i < configuredCount && i < AIGUARD_MAX_PROTECTED_PATHS; i++) {
            if (AiGuardUnicodeStartsWithInsensitive(FileName, gPolicy.ProtectedPaths[i])) {
                result = TRUE;
                break;
            }
        }
    }
    ExReleaseResourceLite(&gPolicyLock);

    if (!configured)
        result = AiGuardUnicodeContainsInsensitive(FileName, gProtectedMarker);

    return result;
}

static BOOLEAN AiGuardProcessIsAllowed(void)
{
    static const PCWSTR fallbackAllowedSuffixes[] = {
        L"\\WINWORD.EXE",
        L"\\EXCEL.EXE",
        L"\\POWERPNT.EXE",
        L"\\AcroRd32.exe"
    };

    PUNICODE_STRING processImage = NULL;
    NTSTATUS status;
    ULONG i;
    ULONG configuredCount;
    BOOLEAN configured;
    BOOLEAN allowed = FALSE;

    if (PsGetCurrentProcessId() == (HANDLE)4)
        return TRUE;

    status = SeLocateProcessImageName(PsGetCurrentProcess(), &processImage);
    if (!NT_SUCCESS(status) || processImage == NULL)
        return FALSE;

    ExAcquireResourceSharedLite(&gPolicyLock, TRUE);
    configured = gPolicyConfigured;
    configuredCount = gPolicy.AllowedApplicationCount;
    if (configured) {
        for (i = 0; i < configuredCount && i < AIGUARD_MAX_ALLOWED_APPS; i++) {
            UNICODE_STRING allowedImage;
            RtlInitUnicodeString(&allowedImage, gPolicy.AllowedApplications[i]);
            if (RtlEqualUnicodeString(processImage, &allowedImage, TRUE)) {
                allowed = TRUE;
                break;
            }
        }
    }
    ExReleaseResourceLite(&gPolicyLock);

    if (!configured) {
        for (i = 0; i < RTL_NUMBER_OF(fallbackAllowedSuffixes); i++) {
            if (AiGuardUnicodeContainsInsensitive(processImage, fallbackAllowedSuffixes[i])) {
                allowed = TRUE;
                break;
            }
        }
    }

    ExFreePool(processImage);
    return allowed;
}

static BOOLEAN AiGuardCreateRequestsContentRead(_In_ PFLT_CALLBACK_DATA Data)
{
    ACCESS_MASK desiredAccess;
    ULONG createOptions;

    createOptions = Data->Iopb->Parameters.Create.Options & 0x00FFFFFF;
    if ((createOptions & FILE_DIRECTORY_FILE) != 0)
        return FALSE;

    if (Data->Iopb->Parameters.Create.SecurityContext == NULL)
        return TRUE;

    desiredAccess = Data->Iopb->Parameters.Create.SecurityContext->DesiredAccess;
    return (desiredAccess & (FILE_READ_DATA | GENERIC_READ | GENERIC_ALL | MAXIMUM_ALLOWED)) != 0;
}

FLT_PREOP_CALLBACK_STATUS AiGuardPreCreate(
    _Inout_ PFLT_CALLBACK_DATA Data,
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _Flt_CompletionContext_Outptr_ PVOID* CompletionContext)
{
    PFLT_FILE_NAME_INFORMATION nameInfo = NULL;
    NTSTATUS status;

    UNREFERENCED_PARAMETER(FltObjects);
    UNREFERENCED_PARAMETER(CompletionContext);

    if (Data->RequestorMode == KernelMode)
        return FLT_PREOP_SUCCESS_NO_CALLBACK;

    if (!AiGuardCreateRequestsContentRead(Data))
        return FLT_PREOP_SUCCESS_NO_CALLBACK;

    status = FltGetFileNameInformation(
        Data,
        FLT_FILE_NAME_NORMALIZED | FLT_FILE_NAME_QUERY_DEFAULT,
        &nameInfo);

    if (!NT_SUCCESS(status) || nameInfo == NULL)
        return FLT_PREOP_SUCCESS_NO_CALLBACK;

    status = FltParseFileNameInformation(nameInfo);
    if (!NT_SUCCESS(status)) {
        FltReleaseFileNameInformation(nameInfo);
        return FLT_PREOP_SUCCESS_NO_CALLBACK;
    }

    if (AiGuardPathIsProtected(&nameInfo->Name) && !AiGuardProcessIsAllowed()) {
        Data->IoStatus.Status = STATUS_ACCESS_DENIED;
        Data->IoStatus.Information = 0;
        FltReleaseFileNameInformation(nameInfo);
        return FLT_PREOP_COMPLETE;
    }

    FltReleaseFileNameInformation(nameInfo);
    return FLT_PREOP_SUCCESS_NO_CALLBACK;
}

NTSTATUS AiGuardConnectNotify(
    _In_ PFLT_PORT ClientPort,
    _In_opt_ PVOID ServerPortCookie,
    _In_reads_bytes_opt_(SizeOfContext) PVOID ConnectionContext,
    _In_ ULONG SizeOfContext,
    _Outptr_result_maybenull_ PVOID* ConnectionCookie)
{
    UNREFERENCED_PARAMETER(ServerPortCookie);
    UNREFERENCED_PARAMETER(ConnectionContext);
    UNREFERENCED_PARAMETER(SizeOfContext);

    if (ConnectionCookie != NULL)
        *ConnectionCookie = NULL;

    if (gClientPort != NULL)
        return STATUS_DEVICE_BUSY;

    gClientPort = ClientPort;
    return STATUS_SUCCESS;
}

VOID AiGuardDisconnectNotify(_In_opt_ PVOID ConnectionCookie)
{
    UNREFERENCED_PARAMETER(ConnectionCookie);
    if (gClientPort != NULL)
        FltCloseClientPort(gFilterHandle, &gClientPort);
}

NTSTATUS AiGuardMessageNotify(
    _In_opt_ PVOID PortCookie,
    _In_reads_bytes_opt_(InputBufferLength) PVOID InputBuffer,
    _In_ ULONG InputBufferLength,
    _Out_writes_bytes_to_opt_(OutputBufferLength, *ReturnOutputBufferLength) PVOID OutputBuffer,
    _In_ ULONG OutputBufferLength,
    _Out_ PULONG ReturnOutputBufferLength)
{
    PAIGUARD_POLICY_MESSAGE incoming;
    ULONG i;

    UNREFERENCED_PARAMETER(PortCookie);
    UNREFERENCED_PARAMETER(OutputBuffer);
    UNREFERENCED_PARAMETER(OutputBufferLength);

    if (ReturnOutputBufferLength != NULL)
        *ReturnOutputBufferLength = 0;

    if (InputBuffer == NULL || InputBufferLength != sizeof(AIGUARD_POLICY_MESSAGE))
        return STATUS_INVALID_PARAMETER;

    incoming = (PAIGUARD_POLICY_MESSAGE)ExAllocatePool2(
        POOL_FLAG_NON_PAGED,
        sizeof(AIGUARD_POLICY_MESSAGE),
        AIGUARD_POOL_TAG);
    if (incoming == NULL)
        return STATUS_INSUFFICIENT_RESOURCES;

    __try {
        RtlCopyMemory(incoming, InputBuffer, sizeof(AIGUARD_POLICY_MESSAGE));
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        ExFreePoolWithTag(incoming, AIGUARD_POOL_TAG);
        return GetExceptionCode();
    }

    if (incoming->Version != AIGUARD_POLICY_VERSION ||
        incoming->ProtectedPathCount > AIGUARD_MAX_PROTECTED_PATHS ||
        incoming->AllowedApplicationCount > AIGUARD_MAX_ALLOWED_APPS) {
        ExFreePoolWithTag(incoming, AIGUARD_POOL_TAG);
        return STATUS_INVALID_PARAMETER;
    }

    for (i = 0; i < AIGUARD_MAX_PROTECTED_PATHS; i++)
        incoming->ProtectedPaths[i][AIGUARD_PATH_CHARS - 1] = L'\0';
    for (i = 0; i < AIGUARD_MAX_ALLOWED_APPS; i++)
        incoming->AllowedApplications[i][AIGUARD_PATH_CHARS - 1] = L'\0';

    ExAcquireResourceExclusiveLite(&gPolicyLock, TRUE);
    RtlCopyMemory(&gPolicy, incoming, sizeof(AIGUARD_POLICY_MESSAGE));
    gPolicyConfigured = TRUE;
    ExReleaseResourceLite(&gPolicyLock);

    ExFreePoolWithTag(incoming, AIGUARD_POOL_TAG);
    return STATUS_SUCCESS;
}

NTSTATUS AiGuardUnload(_In_ FLT_FILTER_UNLOAD_FLAGS Flags)
{
    UNREFERENCED_PARAMETER(Flags);

    if (gServerPort != NULL) {
        FltCloseCommunicationPort(gServerPort);
        gServerPort = NULL;
    }

    if (gClientPort != NULL)
        FltCloseClientPort(gFilterHandle, &gClientPort);

    if (gFilterHandle != NULL) {
        FltUnregisterFilter(gFilterHandle);
        gFilterHandle = NULL;
    }

    ExDeleteResourceLite(&gPolicyLock);
    return STATUS_SUCCESS;
}

NTSTATUS DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath)
{
    NTSTATUS status;
    PSECURITY_DESCRIPTOR securityDescriptor = NULL;
    OBJECT_ATTRIBUTES objectAttributes;
    UNICODE_STRING portName;

    UNREFERENCED_PARAMETER(RegistryPath);

    RtlZeroMemory(&gPolicy, sizeof(gPolicy));
    gPolicyConfigured = FALSE;

    status = ExInitializeResourceLite(&gPolicyLock);
    if (!NT_SUCCESS(status))
        return status;

    status = FltRegisterFilter(DriverObject, &FilterRegistration, &gFilterHandle);
    if (!NT_SUCCESS(status)) {
        ExDeleteResourceLite(&gPolicyLock);
        return status;
    }

    status = FltBuildDefaultSecurityDescriptor(&securityDescriptor, FLT_PORT_ALL_ACCESS);
    if (!NT_SUCCESS(status))
        goto Cleanup;

    RtlInitUnicodeString(&portName, AIGUARD_PORT_NAME);
    InitializeObjectAttributes(
        &objectAttributes,
        &portName,
        OBJ_KERNEL_HANDLE | OBJ_CASE_INSENSITIVE,
        NULL,
        securityDescriptor);

    status = FltCreateCommunicationPort(
        gFilterHandle,
        &gServerPort,
        &objectAttributes,
        NULL,
        AiGuardConnectNotify,
        AiGuardDisconnectNotify,
        AiGuardMessageNotify,
        1);

    FltFreeSecurityDescriptor(securityDescriptor);
    securityDescriptor = NULL;

    if (!NT_SUCCESS(status))
        goto Cleanup;

    status = FltStartFiltering(gFilterHandle);
    if (!NT_SUCCESS(status))
        goto Cleanup;

    return STATUS_SUCCESS;

Cleanup:
    if (securityDescriptor != NULL)
        FltFreeSecurityDescriptor(securityDescriptor);
    if (gServerPort != NULL) {
        FltCloseCommunicationPort(gServerPort);
        gServerPort = NULL;
    }
    if (gFilterHandle != NULL) {
        FltUnregisterFilter(gFilterHandle);
        gFilterHandle = NULL;
    }
    ExDeleteResourceLite(&gPolicyLock);
    return status;
}
