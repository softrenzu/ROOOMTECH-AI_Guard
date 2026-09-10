#include <fltKernel.h>

PFLT_FILTER gFilterHandle = NULL;

static const WCHAR* gProtectedMarker = L"\\AI_Guard_Protected\\";

DRIVER_INITIALIZE DriverEntry;

NTSTATUS
AiGuardUnload(
    _In_ FLT_FILTER_UNLOAD_FLAGS Flags
);

FLT_PREOP_CALLBACK_STATUS
AiGuardPreCreate(
    _Inout_ PFLT_CALLBACK_DATA Data,
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _Flt_CompletionContext_Outptr_ PVOID* CompletionContext
);

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

static BOOLEAN
AiGuardUnicodeContainsInsensitive(
    _In_ PCUNICODE_STRING Haystack,
    _In_ PCWSTR Needle
)
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

static BOOLEAN
AiGuardProcessIsAllowed(void)
{
    static const PCWSTR allowedSuffixes[] = {
        L"\\WINWORD.EXE",
        L"\\EXCEL.EXE",
        L"\\POWERPNT.EXE",
        L"\\AcroRd32.exe"
    };

    PUNICODE_STRING processImage = NULL;
    NTSTATUS status;
    ULONG i;

    if (PsGetCurrentProcessId() == (HANDLE)4)
        return TRUE;

    status = SeLocateProcessImageName(PsGetCurrentProcess(), &processImage);
    if (!NT_SUCCESS(status) || processImage == NULL)
        return FALSE;

    for (i = 0; i < RTL_NUMBER_OF(allowedSuffixes); i++) {
        if (AiGuardUnicodeContainsInsensitive(processImage, allowedSuffixes[i])) {
            ExFreePool(processImage);
            return TRUE;
        }
    }

    ExFreePool(processImage);
    return FALSE;
}

FLT_PREOP_CALLBACK_STATUS
AiGuardPreCreate(
    _Inout_ PFLT_CALLBACK_DATA Data,
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _Flt_CompletionContext_Outptr_ PVOID* CompletionContext
)
{
    PFLT_FILE_NAME_INFORMATION nameInfo = NULL;
    NTSTATUS status;

    UNREFERENCED_PARAMETER(FltObjects);
    UNREFERENCED_PARAMETER(CompletionContext);

    if (Data->RequestorMode == KernelMode)
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

    if (AiGuardUnicodeContainsInsensitive(&nameInfo->Name, gProtectedMarker) &&
        !AiGuardProcessIsAllowed()) {

        Data->IoStatus.Status = STATUS_ACCESS_DENIED;
        Data->IoStatus.Information = 0;
        FltReleaseFileNameInformation(nameInfo);
        return FLT_PREOP_COMPLETE;
    }

    FltReleaseFileNameInformation(nameInfo);
    return FLT_PREOP_SUCCESS_NO_CALLBACK;
}

NTSTATUS
AiGuardUnload(
    _In_ FLT_FILTER_UNLOAD_FLAGS Flags
)
{
    UNREFERENCED_PARAMETER(Flags);

    if (gFilterHandle != NULL) {
        FltUnregisterFilter(gFilterHandle);
        gFilterHandle = NULL;
    }

    return STATUS_SUCCESS;
}

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath
)
{
    NTSTATUS status;

    status = FltRegisterFilter(
        DriverObject,
        &FilterRegistration,
        &gFilterHandle);

    if (!NT_SUCCESS(status))
        return status;

    status = FltStartFiltering(gFilterHandle);
    if (!NT_SUCCESS(status)) {
        FltUnregisterFilter(gFilterHandle);
        gFilterHandle = NULL;
    }

    UNREFERENCED_PARAMETER(RegistryPath);
    return status;
}
