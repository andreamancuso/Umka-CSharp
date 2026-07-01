#define __USE_MINGW_ANSI_STDIO 1

#include <limits.h>
#include <stdarg.h>
#include <stdlib.h>
#include <string.h>

#include "umka_compiler.h"
#include "umka_api.h"
#include "umka_types.h"

#ifdef _WIN32
#define USHIM_EXPORT __declspec(dllexport)
#define USHIM_THREAD_LOCAL __declspec(thread)
#else
#define USHIM_EXPORT __attribute__((visibility("default")))
#define USHIM_THREAD_LOCAL __thread
#endif

#define USHIM_NOT_FOUND 2

typedef int (*UshimManagedCallback)(void *state, UmkaStackSlot *params, UmkaStackSlot *result);

typedef struct
{
    UshimManagedCallback callback;
    void *state;
} UshimCallbackSlot;

typedef struct
{
    int64_t entryOffset;
    UmkaStackSlot *params;
    UmkaStackSlot *result;
    const Type *type;
} UshimFuncContext;

static UmkaStackSlot *ushim_param(UmkaStackSlot *params, int index);
static UmkaStackSlot *ushim_result(UmkaFuncContext *function);
static const Type *ushim_context_get_parameter_type(UmkaFuncContext *function, int index);
static const Type *ushim_context_get_result_type(UmkaFuncContext *function);
static const Type *ushim_callback_get_parameter_type(UmkaStackSlot *params, int index);
static const Type *ushim_callback_get_result_type(UmkaStackSlot *params, UmkaStackSlot *result);
static int ushim_type_kind(const Type *type);
static int ushim_type_size(const Type *type);
static int ushim_type_item_count(const Type *type);
static int ushim_type_has_references(const Type *type);
static int ushim_type_is_enum(const Type *type);
static int ushim_type_enum_member_count(const Type *type);
static const char *ushim_type_enum_member_name(const Type *type, int index);
static int64_t ushim_type_enum_member_signed_value(const Type *type, int index);
static uint64_t ushim_type_enum_member_unsigned_value(const Type *type, int index);
static const Type *ushim_type_element_type(const Type *type);
static const char *ushim_type_name(const Type *type);
static int ushim_type_element_kind(const Type *type);
static int ushim_type_element_size(const Type *type);
static int ushim_type_element_has_references(const Type *type);
static const char *ushim_type_element_name(const Type *type);
static int ushim_type_nested_dynarray_element_kind(const Type *type);
static int ushim_type_nested_dynarray_element_size(const Type *type);
static int ushim_type_nested_dynarray_element_has_references(const Type *type);
static const char *ushim_type_nested_dynarray_element_name(const Type *type);
static int ushim_type_map_key_kind(const Type *type);
static int ushim_type_map_key_size(const Type *type);
static int ushim_type_map_key_has_references(const Type *type);
static const char *ushim_type_map_key_name(const Type *type);
static int ushim_type_map_value_kind(const Type *type);
static int ushim_type_map_value_size(const Type *type);
static int ushim_type_map_value_has_references(const Type *type);
static const char *ushim_type_map_value_name(const Type *type);
static int ushim_type_map_value_element_kind(const Type *type);
static int ushim_type_map_value_element_size(const Type *type);
static int ushim_type_map_value_element_has_references(const Type *type);
static const char *ushim_type_map_value_element_name(const Type *type);
static int ushim_type_is_variadic_parameter_list(const Type *type);
static int ushim_type_uses_indirect_value_slot(const Type *type);
static const Type *ushim_callable_function_type(const Type *type);
static UmkaStackSlot ushim_value_from_storage(const Type *type, UmkaStackSlot *storage);
static void *ushim_callback_result_assignment_target(const Type *type, UmkaStackSlot *slot);
static int ushim_native_value_assign_to_storage(const Type *type, void *dest, UmkaStackSlot value);
static int ushim_native_value_assign_to_interface_storage(Umka *umka, const Type *target, void *dest, const UmkaHostHandle *handle);
static int ushim_native_value_type_matches(const Type *target, const UmkaHostHandle *handle);
static int ushim_native_value_retain(Umka *umka, const Type *type, UmkaStackSlot value, UmkaHostHandle **handle);
static int ushim_type_is_any(const Type *type);
static int ushim_type_is_non_empty_interface(const Type *type);
static UmkaAny *ushim_native_value_any_ptr(const UmkaHostHandle *handle);
static const Type *ushim_native_value_any_payload_type(const UmkaHostHandle *handle, UmkaStackSlot *payload);
static int ushim_any_assign_to_storage(Umka *umka, const Type *targetType, void *dest, const Type *payloadType, UmkaStackSlot payload);

static int ushim_status(Umka *umka)
{
    if (!umka)
        return 1;
    return umka->error.report.code ? umka->error.report.code : 1;
}

static int ushim_try(Umka *umka, int (*body)(Umka *umka, void *data), void *data)
{
    if (!umka)
        return 1;

    if (setjmp(umka->error.jumper) == 0)
        return body(umka, data);

    return ushim_status(umka);
}

static int ushim_type_kind(const Type *type)
{
    return (int)umkaGetTypeKind((const UmkaType *)type);
}

static int ushim_type_size(const Type *type)
{
    return umkaGetTypeSize((const UmkaType *)type);
}

static int ushim_type_item_count(const Type *type)
{
    return umkaGetTypeItemCount((const UmkaType *)type);
}

static int ushim_type_has_references(const Type *type)
{
    return umkaTypeHasReferences((const UmkaType *)type) ? 1 : 0;
}

static int ushim_type_is_enum(const Type *type)
{
    return umkaGetEnumMemberCount((const UmkaType *)type) > 0 ? 1 : 0;
}

static int ushim_type_enum_member_count(const Type *type)
{
    return umkaGetEnumMemberCount((const UmkaType *)type);
}

static const char *ushim_type_enum_member_name(const Type *type, int index)
{
    const char *name = NULL;
    return umkaGetEnumMember((const UmkaType *)type, index, &name, NULL, NULL) ? name : NULL;
}

static int64_t ushim_type_enum_member_signed_value(const Type *type, int index)
{
    int64_t value = 0;
    (void)umkaGetEnumMember((const UmkaType *)type, index, NULL, &value, NULL);
    return value;
}

static uint64_t ushim_type_enum_member_unsigned_value(const Type *type, int index)
{
    uint64_t value = 0;
    (void)umkaGetEnumMember((const UmkaType *)type, index, NULL, NULL, &value);
    return value;
}

static void ushim_managed_callback(UmkaStackSlot *params, UmkaStackSlot *result)
{
    UmkaAny *upvalue = umkaGetUpvalue(params);
    UshimCallbackSlot *slot = upvalue ? (UshimCallbackSlot *)upvalue->data : NULL;
    if (!slot || !slot->callback)
    {
        Umka *umka = umkaGetInstance(result);
        umka->error.runtimeHandler(umka, ERR_RUNTIME, "Managed callback slot is null");
        return;
    }

    int err = slot->callback(slot->state, params, result);
    if (err)
    {
        Umka *umka = umkaGetInstance(result);
        umka->error.runtimeHandler(umka, ERR_RUNTIME, "Managed callback failed");
    }
}

USHIM_EXPORT int ushim_create(
    const char *fileName,
    const char *source,
    int stackSize,
    int argc,
    char **argv,
    int fileSystemEnabled,
    int implLibsEnabled,
    UmkaWarningCallback warningCallback,
    Umka **runtime)
{
    if (!runtime)
        return 1;

    *runtime = umkaAlloc();
    if (!*runtime)
        return 1;

    if (!umkaInit(*runtime, fileName, source, stackSize, NULL, argc, argv, fileSystemEnabled != 0, implLibsEnabled != 0, warningCallback))
        return ushim_status(*runtime);

    return 0;
}

USHIM_EXPORT void ushim_free(Umka *umka)
{
    if (umka)
    {
        /* Embedding cleanup must not abort the host process on Umka LeakSan reports. */
        umka->vm.pages.leakSanLevel = 0;
        umkaFree(umka);
    }
}

USHIM_EXPORT int ushim_compile(Umka *umka)
{
    if (!umkaCompile(umka))
        return ushim_status(umka);
    return 0;
}

USHIM_EXPORT void ushim_clear_warning_callback(Umka *umka)
{
    if (umka)
        umka->error.warningCallback = NULL;
}

USHIM_EXPORT int ushim_run(Umka *umka)
{
    return umkaRun(umka);
}

USHIM_EXPORT int ushim_alive(Umka *umka)
{
    return umka && umkaAlive(umka);
}

USHIM_EXPORT void ushim_request_interrupt(Umka *umka, const char *message)
{
    umkaRequestInterrupt(umka, message);
}

USHIM_EXPORT void ushim_clear_interrupt(Umka *umka)
{
    umkaClearInterrupt(umka);
}

USHIM_EXPORT int ushim_interrupt_requested(Umka *umka)
{
    return umkaInterruptRequested(umka) ? 1 : 0;
}

typedef struct
{
    const char *fileName;
    const char *source;
} AddModuleData;

static int ushim_add_module_body(Umka *umka, void *data)
{
    AddModuleData *module = (AddModuleData *)data;
    return umkaAddModule(umka, module->fileName, module->source) ? 0 : 1;
}

USHIM_EXPORT int ushim_add_module(Umka *umka, const char *fileName, const char *source)
{
    AddModuleData data = {fileName, source};
    return ushim_try(umka, ushim_add_module_body, &data);
}

typedef struct
{
    const char *name;
    UshimManagedCallback callback;
    void *state;
    UshimCallbackSlot **slot;
} AddCallbackData;

static int ushim_add_callback_body(Umka *umka, void *data)
{
    AddCallbackData *callback = (AddCallbackData *)data;
    UshimCallbackSlot *slot = (UshimCallbackSlot *)malloc(sizeof(UshimCallbackSlot));
    if (!slot)
        return 1;

    slot->callback = callback->callback;
    slot->state = callback->state;

    if (!umkaAddClosure(umka, callback->name, ushim_managed_callback, slot))
    {
        free(slot);
        return 1;
    }

    if (callback->slot)
        *callback->slot = slot;

    return 0;
}

USHIM_EXPORT int ushim_add_callback(
    Umka *umka,
    const char *name,
    UshimManagedCallback callback,
    void *state,
    UshimCallbackSlot **slot)
{
    if (slot)
        *slot = NULL;
    AddCallbackData data = {name, callback, state, slot};
    return ushim_try(umka, ushim_add_callback_body, &data);
}

USHIM_EXPORT void ushim_free_callback(UshimCallbackSlot *slot)
{
    free(slot);
}

typedef struct
{
    const char *moduleName;
    const char *functionName;
    UmkaFuncContext *function;
} GetFunctionData;

static int ushim_get_function_body(Umka *umka, void *data)
{
    GetFunctionData *fn = (GetFunctionData *)data;
    int module = 1;
    if (fn->moduleName)
    {
        char modulePath[DEFAULT_STR_LEN + 1] = "";
        moduleAssertRegularizePath(&umka->modules, fn->moduleName, umka->modules.curFolder, modulePath, DEFAULT_STR_LEN + 1);
        module = moduleFind(&umka->modules, modulePath);
    }

    const Ident *fnIdent = identFind(&umka->idents, &umka->modules, &umka->blocks, module, fn->functionName, NULL, false);
    if (!fnIdent || !fnIdent->isExported || fnIdent->kind != IDENT_CONST || ushim_type_kind(fnIdent->type) != TYPE_FN)
        return USHIM_NOT_FOUND;

    identSetUsed(fnIdent);
    compilerMakeFuncContext(umka, fnIdent->type, fnIdent->offset, fn->function);
    ((UshimFuncContext *)fn->function)->type = fnIdent->type;
    return 0;
}

USHIM_EXPORT int ushim_get_function(Umka *umka, const char *moduleName, const char *functionName, UmkaFuncContext *function)
{
    GetFunctionData data = {moduleName, functionName, function};
    return ushim_try(umka, ushim_get_function_body, &data);
}

USHIM_EXPORT int ushim_call(Umka *umka, UmkaFuncContext *function)
{
    return umkaCall(umka, function);
}

USHIM_EXPORT int ushim_context_call_retain_result(Umka *umka, UmkaFuncContext *function, UmkaHostHandle **handle)
{
    if (handle)
        *handle = NULL;

    const Type *type = ushim_context_get_result_type(function);
    if (!umka || !function || !type || ushim_type_kind(type) == TYPE_VOID || !handle)
        return 1;

    void *buffer = NULL;
    const int usesBuffer = ushim_type_uses_indirect_value_slot(type);
    if (usesBuffer)
    {
        const int typeSize = ushim_type_size(type);
        buffer = calloc(1, typeSize > 0 ? (size_t)typeSize : 1);
        if (!buffer)
            return 1;

        function->result->ptrVal = buffer;
    }

    int status = umkaCall(umka, function);
    if (status == 0)
    {
        UmkaStackSlot value = {0};
        if (usesBuffer)
            value.ptrVal = buffer;
        else
            value = *ushim_result(function);
        status = ushim_native_value_retain(umka, type, value, handle);
    }

    if (usesBuffer)
    {
        (void)umkaReleaseHostValue(umka, buffer, (const UmkaType *)type);
        free(buffer);
        function->result->ptrVal = NULL;
    }

    return status;
}

USHIM_EXPORT int ushim_native_value_callable_valid(UmkaHostHandle *handle)
{
    if (!umkaHostHandleValid(handle))
        return 0;

    const Type *type = (const Type *)umkaGetHostHandleType(handle);
    UmkaStackSlot value = umkaGetHostHandleValue(handle);
    return umkaCallableValid((const UmkaType *)type, value) ? 1 : 0;
}

USHIM_EXPORT int ushim_native_value_make_callable_context(Umka *umka, UmkaHostHandle *handle, UmkaFuncContext *function)
{
    if (!umka || !function || !umkaHostHandleValid(handle))
        return 1;

    const Type *type = (const Type *)umkaGetHostHandleType(handle);
    const Type *fnType = ushim_callable_function_type(type);
    if (!fnType)
        return 1;

    UmkaStackSlot value = umkaGetHostHandleValue(handle);
    if (!umkaMakeCallableContext(umka, (const UmkaType *)type, value, function))
        return 1;

    ((UshimFuncContext *)function)->type = fnType;
    return 0;
}

USHIM_EXPORT int ushim_callable_call(Umka *umka, UmkaHostHandle *handle, UmkaFuncContext *function)
{
    if (!umka || !function || !umkaHostHandleValid(handle))
        return 1;

    const Type *type = (const Type *)umkaGetHostHandleType(handle);
    UmkaStackSlot value = umkaGetHostHandleValue(handle);
    return umkaCallCallable(umka, (const UmkaType *)type, value, function);
}

USHIM_EXPORT int ushim_callable_call_retain_result(Umka *umka, UmkaHostHandle *callable, UmkaFuncContext *function, UmkaHostHandle **handle)
{
    if (handle)
        *handle = NULL;

    const Type *type = ushim_context_get_result_type(function);
    if (!umka || !function || !type || ushim_type_kind(type) == TYPE_VOID || !handle || !umkaHostHandleValid(callable))
        return 1;

    void *buffer = NULL;
    const int usesBuffer = ushim_type_uses_indirect_value_slot(type);
    if (usesBuffer)
    {
        const int typeSize = ushim_type_size(type);
        buffer = calloc(1, typeSize > 0 ? (size_t)typeSize : 1);
        if (!buffer)
            return 1;

        function->result->ptrVal = buffer;
    }

    const Type *callableType = (const Type *)umkaGetHostHandleType(callable);
    UmkaStackSlot callableValue = umkaGetHostHandleValue(callable);
    int status = umkaCallCallable(umka, (const UmkaType *)callableType, callableValue, function);
    if (status == 0)
    {
        UmkaStackSlot value = {0};
        if (usesBuffer)
            value.ptrVal = buffer;
        else
            value = *ushim_result(function);
        status = ushim_native_value_retain(umka, type, value, handle);
    }

    if (usesBuffer)
    {
        (void)umkaReleaseHostValue(umka, buffer, (const UmkaType *)type);
        free(buffer);
        function->result->ptrVal = NULL;
    }

    return status;
}

USHIM_EXPORT int ushim_context_set_arg_native_value(
    Umka *umka,
    UmkaFuncContext *function,
    int index,
    UmkaHostHandle *handle)
{
    UmkaStackSlot *slot = ushim_param(function ? function->params : NULL, index);
    const Type *type = ushim_context_get_parameter_type(function, index);
    if (!umka || !slot || !type || !umkaHostHandleValid(handle))
        return 1;

    UmkaStackSlot value = umkaGetHostHandleValue(handle);
    if (ushim_native_value_type_matches(type, handle))
        return ushim_native_value_assign_to_storage(type, slot, value);

    return ushim_native_value_assign_to_interface_storage(umka, type, slot, handle);
}

typedef struct
{
    UmkaFuncContext *function;
    int index;
    int payloadKind;
    UmkaStackSlot payload;
    const char *stringValue;
    UmkaHostHandle *handle;
} SetAnyArgumentData;

static const Type *ushim_predecl_any_payload_type(Umka *umka, int kind)
{
    if (!umka)
        return NULL;

    switch (kind)
    {
        case TYPE_INT:
            return umka->types.predecl.intType;
        case TYPE_UINT:
            return umka->types.predecl.uintType;
        case TYPE_CHAR:
            return umka->types.predecl.charType;
        case TYPE_REAL:
            return umka->types.predecl.realType;
        case TYPE_BOOL:
            return umka->types.predecl.boolType;
        case TYPE_STR:
            return umka->types.predecl.strType;
        case TYPE_NONE:
            return NULL;
        default:
            return NULL;
    }
}

static int ushim_context_set_arg_any_body(Umka *umka, void *data)
{
    SetAnyArgumentData *arg = (SetAnyArgumentData *)data;
    UmkaStackSlot *slot = arg && arg->function ? ushim_param(arg->function->params, arg->index) : NULL;
    const Type *targetType = arg ? ushim_context_get_parameter_type(arg->function, arg->index) : NULL;
    const Type *payloadType = NULL;
    UmkaStackSlot payload = {0};

    if (!arg || !slot || !targetType)
        return 1;

    if (arg->handle)
    {
        if (!umkaHostHandleValid(arg->handle))
            return 1;

        payloadType = (const Type *)umkaGetHostHandleType(arg->handle);
        payload = umkaGetHostHandleValue(arg->handle);
    }
    else
    {
        payloadType = ushim_predecl_any_payload_type(umka, arg->payloadKind);
        if (arg->payloadKind != TYPE_NONE && !payloadType)
            return 1;

        payload = arg->payload;
        if (arg->payloadKind == TYPE_STR)
            payload.ptrVal = arg->stringValue ? umkaMakeStr(umka, arg->stringValue) : NULL;
    }

    const int status = ushim_any_assign_to_storage(umka, targetType, slot, payloadType, payload);

    if (!arg->handle && arg->payloadKind == TYPE_STR && payload.ptrVal)
        umkaDecRef(umka, payload.ptrVal);

    return status;
}

static int ushim_context_set_arg_any(Umka *umka, UmkaFuncContext *function, int index, int payloadKind, UmkaStackSlot payload, const char *stringValue, UmkaHostHandle *handle)
{
    SetAnyArgumentData data = {function, index, payloadKind, payload, stringValue, handle};
    return ushim_try(umka, ushim_context_set_arg_any_body, &data);
}

USHIM_EXPORT int ushim_context_set_arg_any_null(Umka *umka, UmkaFuncContext *function, int index)
{
    return ushim_context_set_arg_any(umka, function, index, TYPE_NONE, (UmkaStackSlot){0}, NULL, NULL);
}

USHIM_EXPORT int ushim_context_set_arg_any_int(Umka *umka, UmkaFuncContext *function, int index, int64_t value)
{
    UmkaStackSlot payload = {0};
    payload.intVal = value;
    return ushim_context_set_arg_any(umka, function, index, TYPE_INT, payload, NULL, NULL);
}

USHIM_EXPORT int ushim_context_set_arg_any_uint(Umka *umka, UmkaFuncContext *function, int index, uint64_t value)
{
    UmkaStackSlot payload = {0};
    payload.uintVal = value;
    return ushim_context_set_arg_any(umka, function, index, TYPE_UINT, payload, NULL, NULL);
}

USHIM_EXPORT int ushim_context_set_arg_any_char(Umka *umka, UmkaFuncContext *function, int index, uint64_t value)
{
    UmkaStackSlot payload = {0};
    payload.uintVal = value;
    return ushim_context_set_arg_any(umka, function, index, TYPE_CHAR, payload, NULL, NULL);
}

USHIM_EXPORT int ushim_context_set_arg_any_real(Umka *umka, UmkaFuncContext *function, int index, double value)
{
    UmkaStackSlot payload = {0};
    payload.realVal = value;
    return ushim_context_set_arg_any(umka, function, index, TYPE_REAL, payload, NULL, NULL);
}

USHIM_EXPORT int ushim_context_set_arg_any_bool(Umka *umka, UmkaFuncContext *function, int index, int value)
{
    UmkaStackSlot payload = {0};
    payload.intVal = value ? 1 : 0;
    return ushim_context_set_arg_any(umka, function, index, TYPE_BOOL, payload, NULL, NULL);
}

USHIM_EXPORT int ushim_context_set_arg_any_string(Umka *umka, UmkaFuncContext *function, int index, const char *value)
{
    return ushim_context_set_arg_any(umka, function, index, TYPE_STR, (UmkaStackSlot){0}, value, NULL);
}

USHIM_EXPORT int ushim_context_set_arg_any_native_value(Umka *umka, UmkaFuncContext *function, int index, UmkaHostHandle *handle)
{
    return ushim_context_set_arg_any(umka, function, index, TYPE_NONE, (UmkaStackSlot){0}, NULL, handle);
}

USHIM_EXPORT void ushim_native_value_release(UmkaHostHandle *handle)
{
    if (handle)
    {
        umkaReleaseHostHandle(handle);
        free(handle);
    }
}

USHIM_EXPORT int ushim_native_value_retain_host_data(Umka *umka, void *ptr, UmkaHostHandle **handle)
{
    if (handle)
        *handle = NULL;
    if (!umka || !ptr || !handle)
        return 1;

    UmkaHostHandle *retained = (UmkaHostHandle *)malloc(sizeof(UmkaHostHandle));
    if (!retained)
        return 1;

    umkaMakeHostHandle(retained);
    if (!umkaRetainHostData(umka, retained, ptr))
    {
        umkaReleaseHostHandle(retained);
        free(retained);
        return 1;
    }

    *handle = retained;
    return 0;
}

USHIM_EXPORT int ushim_native_value_any_state(UmkaHostHandle *handle)
{
    UmkaAny *any = ushim_native_value_any_ptr(handle);
    if (!any)
        return 0;

    const UmkaType *payloadType = NULL;
    UmkaStackSlot payload = {0};
    return umkaGetAnyValue(any, &payloadType, &payload) && payloadType ? 2 : 1;
}

USHIM_EXPORT int ushim_native_value_any_self_state(UmkaHostHandle *handle)
{
    UmkaAny *any = ushim_native_value_any_ptr(handle);
    if (!any)
        return 0;

    const UmkaType *selfType = NULL;
    void *self = NULL;
    return umkaGetAnySelf(any, &selfType, &self) && selfType && self ? 1 : 0;
}

USHIM_EXPORT int ushim_native_value_any_get_payload_kind(UmkaHostHandle *handle)
{
    const Type *type = ushim_native_value_any_payload_type(handle, NULL);
    return ushim_type_kind(type);
}

USHIM_EXPORT int ushim_native_value_any_get_payload_size(UmkaHostHandle *handle)
{
    const Type *type = ushim_native_value_any_payload_type(handle, NULL);
    return ushim_type_size(type);
}

USHIM_EXPORT int ushim_native_value_any_get_payload_item_count(UmkaHostHandle *handle)
{
    const Type *type = ushim_native_value_any_payload_type(handle, NULL);
    return ushim_type_item_count(type);
}

USHIM_EXPORT int ushim_native_value_any_get_payload_has_references(UmkaHostHandle *handle)
{
    const Type *type = ushim_native_value_any_payload_type(handle, NULL);
    return ushim_type_has_references(type);
}

USHIM_EXPORT const char *ushim_native_value_any_get_payload_type_name(UmkaHostHandle *handle)
{
    return ushim_type_name(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_element_kind(UmkaHostHandle *handle)
{
    return ushim_type_element_kind(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_element_size(UmkaHostHandle *handle)
{
    return ushim_type_element_size(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_element_has_references(UmkaHostHandle *handle)
{
    return ushim_type_element_has_references(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT const char *ushim_native_value_any_get_payload_element_type_name(UmkaHostHandle *handle)
{
    return ushim_type_element_name(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_nested_element_kind(UmkaHostHandle *handle)
{
    return ushim_type_nested_dynarray_element_kind(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_nested_element_size(UmkaHostHandle *handle)
{
    return ushim_type_nested_dynarray_element_size(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_nested_element_has_references(UmkaHostHandle *handle)
{
    return ushim_type_nested_dynarray_element_has_references(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT const char *ushim_native_value_any_get_payload_nested_element_type_name(UmkaHostHandle *handle)
{
    return ushim_type_nested_dynarray_element_name(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_map_key_kind(UmkaHostHandle *handle)
{
    return ushim_type_map_key_kind(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_map_key_size(UmkaHostHandle *handle)
{
    return ushim_type_map_key_size(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_map_key_has_references(UmkaHostHandle *handle)
{
    return ushim_type_map_key_has_references(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT const char *ushim_native_value_any_get_payload_map_key_type_name(UmkaHostHandle *handle)
{
    return ushim_type_map_key_name(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_map_value_kind(UmkaHostHandle *handle)
{
    return ushim_type_map_value_kind(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_map_value_size(UmkaHostHandle *handle)
{
    return ushim_type_map_value_size(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_map_value_has_references(UmkaHostHandle *handle)
{
    return ushim_type_map_value_has_references(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT const char *ushim_native_value_any_get_payload_map_value_type_name(UmkaHostHandle *handle)
{
    return ushim_type_map_value_name(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_map_value_element_kind(UmkaHostHandle *handle)
{
    return ushim_type_map_value_element_kind(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_map_value_element_size(UmkaHostHandle *handle)
{
    return ushim_type_map_value_element_size(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_map_value_element_has_references(UmkaHostHandle *handle)
{
    return ushim_type_map_value_element_has_references(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT const char *ushim_native_value_any_get_payload_map_value_element_type_name(UmkaHostHandle *handle)
{
    return ushim_type_map_value_element_name(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_is_variadic_parameter_list(UmkaHostHandle *handle)
{
    return ushim_type_is_variadic_parameter_list(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_is_enum(UmkaHostHandle *handle)
{
    return ushim_type_is_enum(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT int ushim_native_value_any_get_payload_enum_member_count(UmkaHostHandle *handle)
{
    return ushim_type_enum_member_count(ushim_native_value_any_payload_type(handle, NULL));
}

USHIM_EXPORT const char *ushim_native_value_any_get_payload_enum_member_name(UmkaHostHandle *handle, int memberIndex)
{
    return ushim_type_enum_member_name(ushim_native_value_any_payload_type(handle, NULL), memberIndex);
}

USHIM_EXPORT int64_t ushim_native_value_any_get_payload_enum_member_signed_value(UmkaHostHandle *handle, int memberIndex)
{
    return ushim_type_enum_member_signed_value(ushim_native_value_any_payload_type(handle, NULL), memberIndex);
}

USHIM_EXPORT uint64_t ushim_native_value_any_get_payload_enum_member_unsigned_value(UmkaHostHandle *handle, int memberIndex)
{
    return ushim_type_enum_member_unsigned_value(ushim_native_value_any_payload_type(handle, NULL), memberIndex);
}

USHIM_EXPORT int64_t ushim_native_value_any_get_payload_int(UmkaHostHandle *handle)
{
    UmkaStackSlot payload = {0};
    (void)ushim_native_value_any_payload_type(handle, &payload);
    return payload.intVal;
}

USHIM_EXPORT uint64_t ushim_native_value_any_get_payload_uint(UmkaHostHandle *handle)
{
    UmkaStackSlot payload = {0};
    (void)ushim_native_value_any_payload_type(handle, &payload);
    return payload.uintVal;
}

USHIM_EXPORT double ushim_native_value_any_get_payload_real(UmkaHostHandle *handle)
{
    UmkaStackSlot payload = {0};
    const Type *type = ushim_native_value_any_payload_type(handle, &payload);
    return ushim_type_kind(type) == TYPE_REAL32 ? payload.real32Val : payload.realVal;
}

USHIM_EXPORT void *ushim_native_value_any_get_payload_ptr(UmkaHostHandle *handle)
{
    UmkaStackSlot payload = {0};
    (void)ushim_native_value_any_payload_type(handle, &payload);
    return payload.ptrVal;
}

USHIM_EXPORT const char *ushim_native_value_any_get_payload_string(UmkaHostHandle *handle)
{
    UmkaStackSlot payload = {0};
    const Type *type = ushim_native_value_any_payload_type(handle, &payload);
    return ushim_type_kind(type) == TYPE_STR ? (const char *)payload.ptrVal : NULL;
}

USHIM_EXPORT int ushim_native_value_any_retain_payload(Umka *umka, UmkaHostHandle *handle, UmkaHostHandle **payloadHandle)
{
    if (payloadHandle)
        *payloadHandle = NULL;

    UmkaStackSlot payload = {0};
    const Type *type = ushim_native_value_any_payload_type(handle, &payload);
    if (!type || !payloadHandle)
        return 1;

    return ushim_native_value_retain(umka, type, payload, payloadHandle);
}

static UmkaStackSlot *ushim_param(UmkaStackSlot *params, int index)
{
    return params ? umkaGetParam(params, index) : NULL;
}

static const Type *ushim_context_get_function_type(UmkaFuncContext *function)
{
    return function ? ((const UshimFuncContext *)function)->type : NULL;
}

static int ushim_context_get_explicit_argument_count(UmkaFuncContext *function)
{
    if (!function || !function->params)
        return 0;

    int count = 0;
    while (umkaGetParamType(function->params, count))
        count++;
    return count;
}

static int ushim_type_uses_indirect_value_slot(const Type *type)
{
    return umkaTypeUsesIndirectValueSlot((const UmkaType *)type) ? 1 : 0;
}

static const Type *ushim_callable_function_type(const Type *type)
{
    return (const Type *)umkaGetCallableFuncType((const UmkaType *)type);
}

static UmkaStackSlot ushim_value_from_storage(const Type *type, UmkaStackSlot *storage)
{
    UmkaStackSlot value = {0};
    if (!type || !storage)
        return value;

    if (ushim_type_uses_indirect_value_slot(type))
        value.ptrVal = storage;
    else
        value = *storage;

    return value;
}

static void *ushim_callback_result_assignment_target(const Type *type, UmkaStackSlot *slot)
{
    if (!type || !slot)
        return NULL;

    return ushim_type_uses_indirect_value_slot(type) ? slot->ptrVal : slot;
}

static int ushim_native_value_assign_to_storage(const Type *type, void *dest, UmkaStackSlot value)
{
    if (!type || !dest)
        return 1;

    if (ushim_type_uses_indirect_value_slot(type))
    {
        const int typeSize = ushim_type_size(type);
        if (!value.ptrVal || typeSize < 0)
            return 1;

        memcpy(dest, value.ptrVal, (size_t)typeSize);
        return 0;
    }

    *(UmkaStackSlot *)dest = value;
    return 0;
}

static int ushim_native_value_assign_to_interface_storage(Umka *umka, const Type *target, void *dest, const UmkaHostHandle *handle)
{
    if (!umka || !dest || !ushim_type_is_non_empty_interface(target) || !umkaHostHandleValid(handle))
        return 1;

    const Type *source = (const Type *)umkaGetHostHandleType(handle);
    if (ushim_type_kind(source) != TYPE_STRUCT)
        return 1;

    UmkaStackSlot value = umkaGetHostHandleValue(handle);
    return umkaMakeInterface(umka, dest, (const UmkaType *)target, (const UmkaType *)source, value) ? 0 : 1;
}

static int ushim_native_value_type_matches(const Type *target, const UmkaHostHandle *handle)
{
    const Type *source = (const Type *)umkaGetHostHandleType(handle);
    return target && source && umkaTypesEquivalent((const UmkaType *)target, (const UmkaType *)source) ? 1 : 0;
}

static int ushim_type_is_any(const Type *type)
{
    return ushim_type_kind(type) == TYPE_INTERFACE && ushim_type_item_count(type) == 2;
}

static int ushim_type_is_non_empty_interface(const Type *type)
{
    return ushim_type_kind(type) == TYPE_INTERFACE && ushim_type_item_count(type) > 2;
}

static UmkaAny *ushim_native_value_any_ptr(const UmkaHostHandle *handle)
{
    if (!umkaHostHandleValid(handle))
        return NULL;

    const Type *type = (const Type *)umkaGetHostHandleType(handle);
    if (!ushim_type_is_any(type))
        return NULL;

    UmkaStackSlot value = umkaGetHostHandleValue(handle);
    return (UmkaAny *)value.ptrVal;
}

static const Type *ushim_native_value_any_payload_type(const UmkaHostHandle *handle, UmkaStackSlot *payload)
{
    UmkaAny *any = ushim_native_value_any_ptr(handle);
    if (!any)
        return NULL;

    const UmkaType *payloadType = NULL;
    UmkaStackSlot payloadValue = {0};
    if (!umkaGetAnyValue(any, &payloadType, &payloadValue) || !payloadType)
        return NULL;

    if (payload)
        *payload = payloadValue;

    return (const Type *)payloadType;
}

static int ushim_any_assign_to_storage(Umka *umka, const Type *targetType, void *dest, const Type *payloadType, UmkaStackSlot payload)
{
    if (!umka || !dest || !ushim_type_is_any(targetType))
        return 1;

    UmkaAny any = {0};
    if (!umkaMakeAny(umka, &any, (const UmkaType *)payloadType, payload))
        return 1;

    UmkaStackSlot anyValue = {0};
    anyValue.ptrVal = &any;
    const int status = umkaAssignHostValue(umka, dest, (const UmkaType *)targetType, anyValue) ? 0 : 1;
    if (status != 0)
        (void)umkaReleaseHostValue(umka, &any, (const UmkaType *)targetType);
    return status;
}

static int ushim_native_value_retain(Umka *umka, const Type *type, UmkaStackSlot value, UmkaHostHandle **handle)
{
    if (handle)
        *handle = NULL;
    if (!umka || !type || !handle)
        return 1;

    UmkaHostHandle *retained = (UmkaHostHandle *)malloc(sizeof(UmkaHostHandle));
    if (!retained)
        return 1;

    umkaMakeHostHandle(retained);
    if (!umkaRetainHostValue(umka, retained, (const UmkaType *)type, value))
    {
        umkaReleaseHostHandle(retained);
        free(retained);
        return 1;
    }

    *handle = retained;
    return 0;
}

USHIM_EXPORT int ushim_context_set_arg_int(Umka *umka, UmkaFuncContext *function, int index, int64_t value)
{
    (void)umka;
    UmkaStackSlot *slot = function ? ushim_param(function->params, index) : NULL;
    if (!slot)
        return 1;
    slot->intVal = value;
    return 0;
}

USHIM_EXPORT int ushim_context_set_arg_uint(Umka *umka, UmkaFuncContext *function, int index, uint64_t value)
{
    (void)umka;
    UmkaStackSlot *slot = function ? ushim_param(function->params, index) : NULL;
    if (!slot)
        return 1;
    slot->uintVal = value;
    return 0;
}

USHIM_EXPORT int ushim_context_set_arg_real(Umka *umka, UmkaFuncContext *function, int index, double value)
{
    (void)umka;
    UmkaStackSlot *slot = function ? ushim_param(function->params, index) : NULL;
    if (!slot)
        return 1;

    const Type *type = ushim_context_get_parameter_type(function, index);
    if (ushim_type_kind(type) == TYPE_REAL32)
        slot->real32Val = (float)value;
    else
        slot->realVal = value;

    return 0;
}

USHIM_EXPORT int ushim_context_set_arg_ptr(Umka *umka, UmkaFuncContext *function, int index, void *value)
{
    (void)umka;
    UmkaStackSlot *slot = function ? ushim_param(function->params, index) : NULL;
    if (!slot)
        return 1;
    slot->ptrVal = value;
    return 0;
}

typedef struct
{
    UmkaFuncContext *function;
    int index;
    const char *value;
} SetStringData;

static int ushim_context_set_arg_string_body(Umka *umka, void *data)
{
    SetStringData *arg = (SetStringData *)data;
    UmkaStackSlot *slot = arg->function ? ushim_param(arg->function->params, arg->index) : NULL;
    if (!slot)
        return 1;
    slot->ptrVal = arg->value ? umkaMakeStr(umka, arg->value) : NULL;
    return 0;
}

USHIM_EXPORT int ushim_context_set_arg_string(Umka *umka, UmkaFuncContext *function, int index, const char *value)
{
    SetStringData data = {function, index, value};
    return ushim_try(umka, ushim_context_set_arg_string_body, &data);
}

static UmkaStackSlot *ushim_result(UmkaFuncContext *function)
{
    if (!function || !function->result)
        return NULL;
    return umkaGetResult(function->params, function->result);
}

USHIM_EXPORT int64_t ushim_context_get_result_int(UmkaFuncContext *function)
{
    UmkaStackSlot *slot = ushim_result(function);
    return slot ? slot->intVal : 0;
}

USHIM_EXPORT uint64_t ushim_context_get_result_uint(UmkaFuncContext *function)
{
    UmkaStackSlot *slot = ushim_result(function);
    return slot ? slot->uintVal : 0;
}

USHIM_EXPORT double ushim_context_get_result_real(UmkaFuncContext *function)
{
    UmkaStackSlot *slot = ushim_result(function);
    return slot ? slot->realVal : 0.0;
}

USHIM_EXPORT void *ushim_context_get_result_ptr(UmkaFuncContext *function)
{
    UmkaStackSlot *slot = ushim_result(function);
    return slot ? slot->ptrVal : NULL;
}

USHIM_EXPORT int ushim_context_get_result_size(UmkaFuncContext *function)
{
    if (!function || !function->params)
        return 0;

    const UmkaType *type = umkaGetResultType(function->params, function->result);
    return umkaGetTypeSize(type);
}

USHIM_EXPORT int ushim_context_get_result_item_count(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_result_type(function);
    return ushim_type_item_count(type);
}

USHIM_EXPORT int ushim_context_get_argument_count(UmkaFuncContext *function)
{
    return ushim_context_get_explicit_argument_count(function);
}

USHIM_EXPORT int ushim_context_get_default_argument_count(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_function_type(function);
    return umkaGetFuncDefaultParamCount((const UmkaType *)type);
}

USHIM_EXPORT int ushim_context_get_required_argument_count(UmkaFuncContext *function)
{
    const int explicitCount = ushim_context_get_explicit_argument_count(function);
    const int defaultCount = ushim_context_get_default_argument_count(function);
    return defaultCount > explicitCount ? explicitCount : explicitCount - defaultCount;
}

static const Type *ushim_context_get_parameter_type(UmkaFuncContext *function, int index)
{
    if (!function || !function->params)
        return NULL;

    return (const Type *)umkaGetParamType(function->params, index);
}

static int ushim_context_set_default_argument(Umka *umka, UmkaFuncContext *function, int index)
{
    const Type *fnType = ushim_context_get_function_type(function);
    if (ushim_type_kind(fnType) != TYPE_FN)
        return 1;

    const int explicitCount = ushim_context_get_explicit_argument_count(function);
    if (index < 0 || index >= explicitCount)
        return 1;

    const int sigIndex = index + 1;    // Skip the hidden upvalue/self slot.
    /*
     * Umka's public API exposes default parameter counts but not the raw
     * default Const values, so this is intentionally isolated until the fork
     * provides a helper that applies defaults to an UmkaFuncContext.
     */
    const Param *param = fnType->sig->param[sigIndex];
    const Type *type = param ? (const Type *)umkaGetFuncParamType((const UmkaType *)fnType, index) : NULL;
    UmkaStackSlot *slot = ushim_param(function->params, index);
    if (!type || !slot)
        return 1;

    const Const value = param->defaultVal;
    switch (ushim_type_kind(type))
    {
        case TYPE_INT8:
        case TYPE_INT16:
        case TYPE_INT32:
        case TYPE_INT:
        case TYPE_BOOL:
            slot->intVal = value.intVal;
            return 0;

        case TYPE_UINT8:
        case TYPE_UINT16:
        case TYPE_UINT32:
        case TYPE_CHAR:
            slot->uintVal = (uint64_t)value.intVal;
            return 0;

        case TYPE_UINT:
            slot->uintVal = value.uintVal;
            return 0;

        case TYPE_REAL32:
            slot->real32Val = (float)value.realVal;
            return 0;

        case TYPE_REAL:
            slot->realVal = value.realVal;
            return 0;

        case TYPE_PTR:
            slot->ptrVal = value.ptrVal;
            return 0;

        case TYPE_STR:
            slot->ptrVal = value.ptrVal ? umkaMakeStr(umka, (const char *)value.ptrVal) : NULL;
            return 0;

        default:
            return 1;
    }
}

USHIM_EXPORT int ushim_context_set_default_arguments(Umka *umka, UmkaFuncContext *function, int providedCount)
{
    const int explicitCount = ushim_context_get_explicit_argument_count(function);
    const int requiredCount = ushim_context_get_required_argument_count(function);
    if (providedCount < requiredCount || providedCount > explicitCount)
        return 1;

    for (int i = providedCount; i < explicitCount; i++)
        if (ushim_context_set_default_argument(umka, function, i) != 0)
            return 1;

    return 0;
}

static const Type *ushim_context_get_result_type(UmkaFuncContext *function)
{
    if (!function || !function->params)
        return NULL;

    return (const Type *)umkaGetResultType(function->params, function->result);
}

static const char *ushim_type_name(const Type *type)
{
    static USHIM_THREAD_LOCAL char buf[DEFAULT_STR_LEN + 1];
    if (!type)
        return NULL;

    buf[0] = '\0';
    (void)umkaGetTypeSpelling((const UmkaType *)type, buf, DEFAULT_STR_LEN + 1);
    return buf;
}

static const Type *ushim_type_element_type(const Type *type)
{
    const int kind = ushim_type_kind(type);
    return kind == TYPE_ARRAY || kind == TYPE_DYNARRAY ? (const Type *)umkaGetBaseType((const UmkaType *)type) : NULL;
}

static int ushim_type_element_kind(const Type *type)
{
    const Type *elementType = ushim_type_element_type(type);
    return ushim_type_kind(elementType);
}

static int ushim_type_element_size(const Type *type)
{
    const Type *elementType = ushim_type_element_type(type);
    return ushim_type_size(elementType);
}

static int ushim_type_element_has_references(const Type *type)
{
    const Type *elementType = ushim_type_element_type(type);
    return ushim_type_has_references(elementType);
}

static const char *ushim_type_element_name(const Type *type)
{
    return ushim_type_name(ushim_type_element_type(type));
}

static const Type *ushim_type_nested_dynarray_element_type(const Type *type)
{
    const Type *elementType = ushim_type_element_type(type);
    if (ushim_type_kind(type) != TYPE_DYNARRAY || ushim_type_kind(elementType) != TYPE_DYNARRAY)
        return NULL;

    return ushim_type_element_type(elementType);
}

static int ushim_type_nested_dynarray_element_kind(const Type *type)
{
    const Type *elementType = ushim_type_nested_dynarray_element_type(type);
    return ushim_type_kind(elementType);
}

static int ushim_type_nested_dynarray_element_size(const Type *type)
{
    const Type *elementType = ushim_type_nested_dynarray_element_type(type);
    return ushim_type_size(elementType);
}

static int ushim_type_nested_dynarray_element_has_references(const Type *type)
{
    const Type *elementType = ushim_type_nested_dynarray_element_type(type);
    return ushim_type_has_references(elementType);
}

static const char *ushim_type_nested_dynarray_element_name(const Type *type)
{
    return ushim_type_name(ushim_type_nested_dynarray_element_type(type));
}

static const Type *ushim_type_map_key_type(const Type *type)
{
    return (const Type *)umkaGetMapKeyType((const UmkaType *)type);
}

static const Type *ushim_type_map_value_type(const Type *type)
{
    return (const Type *)umkaGetMapItemType((const UmkaType *)type);
}

static int ushim_type_map_key_kind(const Type *type)
{
    const Type *keyType = ushim_type_map_key_type(type);
    return ushim_type_kind(keyType);
}

static int ushim_type_map_value_kind(const Type *type)
{
    const Type *valueType = ushim_type_map_value_type(type);
    return ushim_type_kind(valueType);
}

static int ushim_type_map_key_size(const Type *type)
{
    const Type *keyType = ushim_type_map_key_type(type);
    return ushim_type_size(keyType);
}

static int ushim_type_map_value_size(const Type *type)
{
    const Type *valueType = ushim_type_map_value_type(type);
    return ushim_type_size(valueType);
}

static int ushim_type_map_key_has_references(const Type *type)
{
    const Type *keyType = ushim_type_map_key_type(type);
    return ushim_type_has_references(keyType);
}

static int ushim_type_map_value_has_references(const Type *type)
{
    const Type *valueType = ushim_type_map_value_type(type);
    return ushim_type_has_references(valueType);
}

static const char *ushim_type_map_key_name(const Type *type)
{
    return ushim_type_name(ushim_type_map_key_type(type));
}

static const char *ushim_type_map_value_name(const Type *type)
{
    return ushim_type_name(ushim_type_map_value_type(type));
}

static int ushim_type_map_value_element_kind(const Type *type)
{
    return ushim_type_element_kind(ushim_type_map_value_type(type));
}

static int ushim_type_map_value_element_size(const Type *type)
{
    return ushim_type_element_size(ushim_type_map_value_type(type));
}

static int ushim_type_map_value_element_has_references(const Type *type)
{
    return ushim_type_element_has_references(ushim_type_map_value_type(type));
}

static const char *ushim_type_map_value_element_name(const Type *type)
{
    return ushim_type_element_name(ushim_type_map_value_type(type));
}

static int ushim_type_is_variadic_parameter_list(const Type *type)
{
    return umkaTypeIsVariadicParamList((const UmkaType *)type) ? 1 : 0;
}

USHIM_EXPORT int ushim_context_get_parameter_kind(UmkaFuncContext *function, int index)
{
    const Type *type = ushim_context_get_parameter_type(function, index);
    return ushim_type_kind(type);
}

USHIM_EXPORT int ushim_context_get_parameter_size(UmkaFuncContext *function, int index)
{
    const Type *type = ushim_context_get_parameter_type(function, index);
    return ushim_type_size(type);
}

USHIM_EXPORT int ushim_context_get_parameter_item_count(UmkaFuncContext *function, int index)
{
    const Type *type = ushim_context_get_parameter_type(function, index);
    return ushim_type_item_count(type);
}

USHIM_EXPORT int ushim_context_get_parameter_element_kind(UmkaFuncContext *function, int index)
{
    return ushim_type_element_kind(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_element_size(UmkaFuncContext *function, int index)
{
    return ushim_type_element_size(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_element_has_references(UmkaFuncContext *function, int index)
{
    return ushim_type_element_has_references(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT const char *ushim_context_get_parameter_element_type_name(UmkaFuncContext *function, int index)
{
    return ushim_type_element_name(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_nested_element_kind(UmkaFuncContext *function, int index)
{
    return ushim_type_nested_dynarray_element_kind(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_nested_element_size(UmkaFuncContext *function, int index)
{
    return ushim_type_nested_dynarray_element_size(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_nested_element_has_references(UmkaFuncContext *function, int index)
{
    return ushim_type_nested_dynarray_element_has_references(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT const char *ushim_context_get_parameter_nested_element_type_name(UmkaFuncContext *function, int index)
{
    return ushim_type_nested_dynarray_element_name(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_map_key_kind(UmkaFuncContext *function, int index)
{
    return ushim_type_map_key_kind(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_map_key_size(UmkaFuncContext *function, int index)
{
    return ushim_type_map_key_size(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_map_key_has_references(UmkaFuncContext *function, int index)
{
    return ushim_type_map_key_has_references(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT const char *ushim_context_get_parameter_map_key_type_name(UmkaFuncContext *function, int index)
{
    return ushim_type_map_key_name(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_map_value_kind(UmkaFuncContext *function, int index)
{
    return ushim_type_map_value_kind(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_map_value_size(UmkaFuncContext *function, int index)
{
    return ushim_type_map_value_size(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_map_value_has_references(UmkaFuncContext *function, int index)
{
    return ushim_type_map_value_has_references(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT const char *ushim_context_get_parameter_map_value_type_name(UmkaFuncContext *function, int index)
{
    return ushim_type_map_value_name(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_map_value_element_kind(UmkaFuncContext *function, int index)
{
    return ushim_type_map_value_element_kind(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_map_value_element_size(UmkaFuncContext *function, int index)
{
    return ushim_type_map_value_element_size(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_map_value_element_has_references(UmkaFuncContext *function, int index)
{
    return ushim_type_map_value_element_has_references(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT const char *ushim_context_get_parameter_map_value_element_type_name(UmkaFuncContext *function, int index)
{
    return ushim_type_map_value_element_name(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_is_variadic_parameter_list(UmkaFuncContext *function, int index)
{
    return ushim_type_is_variadic_parameter_list(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_has_references(UmkaFuncContext *function, int index)
{
    const Type *type = ushim_context_get_parameter_type(function, index);
    return ushim_type_has_references(type);
}

USHIM_EXPORT int ushim_context_get_result_has_references(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_result_type(function);
    return ushim_type_has_references(type);
}

USHIM_EXPORT const char *ushim_context_get_parameter_type_name(UmkaFuncContext *function, int index)
{
    return ushim_type_name(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_is_enum(UmkaFuncContext *function, int index)
{
    return ushim_type_is_enum(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_parameter_enum_member_count(UmkaFuncContext *function, int index)
{
    return ushim_type_enum_member_count(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT const char *ushim_context_get_parameter_enum_member_name(UmkaFuncContext *function, int parameterIndex, int memberIndex)
{
    return ushim_type_enum_member_name(ushim_context_get_parameter_type(function, parameterIndex), memberIndex);
}

USHIM_EXPORT int64_t ushim_context_get_parameter_enum_member_signed_value(UmkaFuncContext *function, int parameterIndex, int memberIndex)
{
    return ushim_type_enum_member_signed_value(ushim_context_get_parameter_type(function, parameterIndex), memberIndex);
}

USHIM_EXPORT uint64_t ushim_context_get_parameter_enum_member_unsigned_value(UmkaFuncContext *function, int parameterIndex, int memberIndex)
{
    return ushim_type_enum_member_unsigned_value(ushim_context_get_parameter_type(function, parameterIndex), memberIndex);
}

USHIM_EXPORT int ushim_context_get_result_kind(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_result_type(function);
    return type ? ushim_type_kind(type) : TYPE_VOID;
}

USHIM_EXPORT int ushim_context_get_result_element_kind(UmkaFuncContext *function)
{
    return ushim_type_element_kind(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_element_size(UmkaFuncContext *function)
{
    return ushim_type_element_size(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_element_has_references(UmkaFuncContext *function)
{
    return ushim_type_element_has_references(ushim_context_get_result_type(function));
}

USHIM_EXPORT const char *ushim_context_get_result_element_type_name(UmkaFuncContext *function)
{
    return ushim_type_element_name(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_nested_element_kind(UmkaFuncContext *function)
{
    return ushim_type_nested_dynarray_element_kind(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_nested_element_size(UmkaFuncContext *function)
{
    return ushim_type_nested_dynarray_element_size(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_nested_element_has_references(UmkaFuncContext *function)
{
    return ushim_type_nested_dynarray_element_has_references(ushim_context_get_result_type(function));
}

USHIM_EXPORT const char *ushim_context_get_result_nested_element_type_name(UmkaFuncContext *function)
{
    return ushim_type_nested_dynarray_element_name(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_map_key_kind(UmkaFuncContext *function)
{
    return ushim_type_map_key_kind(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_map_key_size(UmkaFuncContext *function)
{
    return ushim_type_map_key_size(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_map_key_has_references(UmkaFuncContext *function)
{
    return ushim_type_map_key_has_references(ushim_context_get_result_type(function));
}

USHIM_EXPORT const char *ushim_context_get_result_map_key_type_name(UmkaFuncContext *function)
{
    return ushim_type_map_key_name(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_map_value_kind(UmkaFuncContext *function)
{
    return ushim_type_map_value_kind(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_map_value_size(UmkaFuncContext *function)
{
    return ushim_type_map_value_size(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_map_value_has_references(UmkaFuncContext *function)
{
    return ushim_type_map_value_has_references(ushim_context_get_result_type(function));
}

USHIM_EXPORT const char *ushim_context_get_result_map_value_type_name(UmkaFuncContext *function)
{
    return ushim_type_map_value_name(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_map_value_element_kind(UmkaFuncContext *function)
{
    return ushim_type_map_value_element_kind(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_map_value_element_size(UmkaFuncContext *function)
{
    return ushim_type_map_value_element_size(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_map_value_element_has_references(UmkaFuncContext *function)
{
    return ushim_type_map_value_element_has_references(ushim_context_get_result_type(function));
}

USHIM_EXPORT const char *ushim_context_get_result_map_value_element_type_name(UmkaFuncContext *function)
{
    return ushim_type_map_value_element_name(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_is_variadic_parameter_list(UmkaFuncContext *function)
{
    return ushim_type_is_variadic_parameter_list(ushim_context_get_result_type(function));
}

USHIM_EXPORT const char *ushim_context_get_result_type_name(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_result_type(function);
    return type ? ushim_type_name(type) : "void";
}

USHIM_EXPORT int ushim_context_get_result_is_enum(UmkaFuncContext *function)
{
    return ushim_type_is_enum(ushim_context_get_result_type(function));
}

USHIM_EXPORT int ushim_context_get_result_enum_member_count(UmkaFuncContext *function)
{
    return ushim_type_enum_member_count(ushim_context_get_result_type(function));
}

USHIM_EXPORT const char *ushim_context_get_result_enum_member_name(UmkaFuncContext *function, int memberIndex)
{
    return ushim_type_enum_member_name(ushim_context_get_result_type(function), memberIndex);
}

USHIM_EXPORT int64_t ushim_context_get_result_enum_member_signed_value(UmkaFuncContext *function, int memberIndex)
{
    return ushim_type_enum_member_signed_value(ushim_context_get_result_type(function), memberIndex);
}

USHIM_EXPORT uint64_t ushim_context_get_result_enum_member_unsigned_value(UmkaFuncContext *function, int memberIndex)
{
    return ushim_type_enum_member_unsigned_value(ushim_context_get_result_type(function), memberIndex);
}

USHIM_EXPORT int ushim_context_set_result_buffer(UmkaFuncContext *function, void *buffer)
{
    if (!function || !function->result)
        return 1;

    function->result->ptrVal = buffer;
    return 0;
}

USHIM_EXPORT int ushim_context_set_arg_data(Umka *umka, UmkaFuncContext *function, int index, const void *value, int size)
{
    (void)umka;
    UmkaStackSlot *slot = function ? ushim_param(function->params, index) : NULL;
    const Type *type = ushim_context_get_parameter_type(function, index);
    if (!slot || !type || !value || size != ushim_type_size(type) || ushim_type_has_references(type))
        return 1;

    memcpy(slot, value, size);
    return 0;
}

static int ushim_dynarray_byte_count(const DynArray *array, int *byteCount)
{
    if (!array || !array->type || !ushim_type_element_type((const Type *)array->type) || !byteCount)
        return 1;

    const int len = umkaGetDynArrayLen(array);
    if (len < 0 || array->itemSize < 0 || array->itemSize > INT_MAX)
        return 1;

    const int64_t bytes = (int64_t)len * array->itemSize;
    if (bytes > INT_MAX)
        return 1;

    *byteCount = (int)bytes;
    return 0;
}

static int ushim_dynarray_type_accepts_bytes(const Type *type, int elementSize)
{
    const Type *elementType = ushim_type_element_type(type);
    if (ushim_type_kind(type) != TYPE_DYNARRAY || !elementType || elementSize <= 0)
        return 0;

    if (ushim_type_has_references(elementType) || ushim_type_size(elementType) != elementSize)
        return 0;

    return 1;
}

static int ushim_dynarray_type_accepts_strings(const Type *type)
{
    const Type *elementType = ushim_type_element_type(type);
    return type
        && ushim_type_kind(type) == TYPE_DYNARRAY
        && elementType
        && ushim_type_kind(elementType) == TYPE_STR
        && ushim_type_size(elementType) == (int)sizeof(char *);
}

static int ushim_dynarray_type_accepts_nested_bytes(const Type *type, int elementSize)
{
    const Type *outerElementType = ushim_type_element_type(type);
    const Type *innerElementType = ushim_type_nested_dynarray_element_type(type);
    if (!type
        || ushim_type_kind(type) != TYPE_DYNARRAY
        || !outerElementType
        || ushim_type_kind(outerElementType) != TYPE_DYNARRAY
        || ushim_type_size(outerElementType) != (int)sizeof(DynArray)
        || !innerElementType
        || elementSize <= 0)
    {
        return 0;
    }

    if (ushim_type_has_references(innerElementType) || ushim_type_size(innerElementType) != elementSize)
        return 0;

    return 1;
}

static int ushim_dynarray_type_accepts_nested_strings(const Type *type)
{
    const Type *outerElementType = ushim_type_element_type(type);
    const Type *innerElementType = ushim_type_nested_dynarray_element_type(type);
    return type
        && ushim_type_kind(type) == TYPE_DYNARRAY
        && outerElementType
        && ushim_type_kind(outerElementType) == TYPE_DYNARRAY
        && ushim_type_size(outerElementType) == (int)sizeof(DynArray)
        && innerElementType
        && ushim_type_kind(innerElementType) == TYPE_STR
        && ushim_type_size(innerElementType) == (int)sizeof(char *);
}

static int ushim_map_slot_type_accepts_host_value(const Type *type)
{
    if (!type)
        return 0;

    switch (ushim_type_kind(type))
    {
        case TYPE_INT8:
        case TYPE_INT16:
        case TYPE_INT32:
        case TYPE_INT:
        case TYPE_UINT8:
        case TYPE_UINT16:
        case TYPE_UINT32:
        case TYPE_UINT:
        case TYPE_BOOL:
        case TYPE_CHAR:
        case TYPE_REAL32:
        case TYPE_REAL:
        case TYPE_PTR:
        case TYPE_WEAKPTR:
        case TYPE_STR:
            return 1;

        case TYPE_ARRAY:
        case TYPE_STRUCT:
            return !ushim_type_has_references(type);

        default:
            return 0;
    }
}

static int ushim_map_slot_type_accepts_bytes(const Type *type, int size)
{
    return type
        && ushim_type_kind(type) != TYPE_STR
        && ushim_map_slot_type_accepts_host_value(type)
        && size == ushim_type_size(type);
}

static int ushim_map_type_accepts_bytes(const Type *type, int keySize, int valueSize)
{
    return type
        && ushim_type_kind(type) == TYPE_MAP
        && ushim_map_slot_type_accepts_bytes(ushim_type_map_key_type(type), keySize)
        && ushim_map_slot_type_accepts_bytes(ushim_type_map_value_type(type), valueSize);
}

static int ushim_map_type_accepts_string_key_bytes_value(const Type *type, int valueSize)
{
    const Type *keyType = ushim_type_map_key_type(type);
    return type
        && ushim_type_kind(type) == TYPE_MAP
        && keyType
        && ushim_type_kind(keyType) == TYPE_STR
        && ushim_type_size(keyType) == (int)sizeof(char *)
        && ushim_map_slot_type_accepts_bytes(ushim_type_map_value_type(type), valueSize);
}

static int ushim_map_type_accepts_bytes_key_string_value(const Type *type, int keySize)
{
    const Type *valueType = ushim_type_map_value_type(type);
    return type
        && ushim_type_kind(type) == TYPE_MAP
        && ushim_map_slot_type_accepts_bytes(ushim_type_map_key_type(type), keySize)
        && valueType
        && ushim_type_kind(valueType) == TYPE_STR
        && ushim_type_size(valueType) == (int)sizeof(char *);
}

static int ushim_map_type_accepts_string_key_string_value(const Type *type)
{
    const Type *keyType = ushim_type_map_key_type(type);
    const Type *valueType = ushim_type_map_value_type(type);
    return type
        && ushim_type_kind(type) == TYPE_MAP
        && keyType
        && ushim_type_kind(keyType) == TYPE_STR
        && ushim_type_size(keyType) == (int)sizeof(char *)
        && valueType
        && ushim_type_kind(valueType) == TYPE_STR
        && ushim_type_size(valueType) == (int)sizeof(char *);
}

static int ushim_count_from_bytes(int byteCount, int itemSize, int *count)
{
    if (!count)
        return 1;

    *count = 0;
    if (byteCount < 0 || itemSize <= 0 || byteCount % itemSize != 0)
        return 1;

    *count = byteCount / itemSize;
    return 0;
}

static int ushim_make_slot_from_bytes(const Type *type, const void *bytes, int size, UmkaStackSlot *slot)
{
    if (!type || !bytes || !slot || size != ushim_type_size(type))
        return 1;

    memset(slot, 0, sizeof(*slot));

    switch (ushim_type_kind(type))
    {
        case TYPE_INT8:
        {
            int8_t value = 0;
            memcpy(&value, bytes, sizeof(value));
            slot->intVal = value;
            return 0;
        }
        case TYPE_INT16:
        {
            int16_t value = 0;
            memcpy(&value, bytes, sizeof(value));
            slot->intVal = value;
            return 0;
        }
        case TYPE_INT32:
        {
            int32_t value = 0;
            memcpy(&value, bytes, sizeof(value));
            slot->intVal = value;
            return 0;
        }
        case TYPE_INT:
        {
            int64_t value = 0;
            memcpy(&value, bytes, sizeof(value));
            slot->intVal = value;
            return 0;
        }
        case TYPE_UINT8:
        {
            uint8_t value = 0;
            memcpy(&value, bytes, sizeof(value));
            slot->intVal = value;
            return 0;
        }
        case TYPE_UINT16:
        {
            uint16_t value = 0;
            memcpy(&value, bytes, sizeof(value));
            slot->intVal = value;
            return 0;
        }
        case TYPE_UINT32:
        {
            uint32_t value = 0;
            memcpy(&value, bytes, sizeof(value));
            slot->intVal = value;
            return 0;
        }
        case TYPE_UINT:
        {
            uint64_t value = 0;
            memcpy(&value, bytes, sizeof(value));
            slot->uintVal = value;
            return 0;
        }
        case TYPE_BOOL:
        {
            bool value = false;
            memcpy(&value, bytes, sizeof(value));
            slot->intVal = value ? 1 : 0;
            return 0;
        }
        case TYPE_CHAR:
        {
            unsigned char value = 0;
            memcpy(&value, bytes, sizeof(value));
            slot->intVal = value;
            return 0;
        }
        case TYPE_REAL32:
        {
            float value = 0;
            memcpy(&value, bytes, sizeof(value));
            slot->realVal = value;
            return 0;
        }
        case TYPE_REAL:
        {
            double value = 0;
            memcpy(&value, bytes, sizeof(value));
            slot->realVal = value;
            return 0;
        }
        case TYPE_PTR:
        {
            void *value = NULL;
            memcpy(&value, bytes, sizeof(value));
            slot->ptrVal = value;
            return 0;
        }
        case TYPE_WEAKPTR:
        {
            uint64_t value = 0;
            memcpy(&value, bytes, sizeof(value));
            slot->uintVal = value;
            return 0;
        }
        case TYPE_ARRAY:
        case TYPE_STRUCT:
            slot->ptrVal = (void *)bytes;
            return 0;

        default:
            return 1;
    }
}

static int ushim_map_set_bytes_item(
    Umka *umka,
    Map *map,
    const Type *type,
    const void *key,
    const void *value)
{
    const Type *keyType = ushim_type_map_key_type(type);
    const Type *valueType = ushim_type_map_value_type(type);
    const int keySize = ushim_type_size(keyType);
    const int valueSize = ushim_type_size(valueType);
    UmkaStackSlot keySlot = {0};
    UmkaStackSlot valueSlot = {0};
    if (ushim_make_slot_from_bytes(keyType, key, keySize, &keySlot) != 0
        || ushim_make_slot_from_bytes(valueType, value, valueSize, &valueSlot) != 0)
    {
        return 1;
    }

    return umkaSetMapItem(umka, (UmkaMap *)map, keySlot, valueSlot) ? 0 : 1;
}

static int ushim_map_set_string_key_bytes_value_item(
    Umka *umka,
    Map *map,
    const Type *type,
    const char *key,
    const void *value)
{
    const Type *valueType = ushim_type_map_value_type(type);
    const int valueSize = ushim_type_size(valueType);
    UmkaStackSlot keySlot = {0};
    UmkaStackSlot valueSlot = {0};
    keySlot.ptrVal = key ? umkaMakeStr(umka, key) : NULL;

    int status = 1;
    if (ushim_make_slot_from_bytes(valueType, value, valueSize, &valueSlot) == 0)
        status = umkaSetMapItem(umka, (UmkaMap *)map, keySlot, valueSlot) ? 0 : 1;

    if (keySlot.ptrVal)
        umkaDecRef(umka, keySlot.ptrVal);

    return status;
}

static int ushim_map_set_bytes_key_string_value_item(
    Umka *umka,
    Map *map,
    const Type *type,
    const void *key,
    const char *value)
{
    const Type *keyType = ushim_type_map_key_type(type);
    const int keySize = ushim_type_size(keyType);
    UmkaStackSlot keySlot = {0};
    UmkaStackSlot valueSlot = {0};
    valueSlot.ptrVal = value ? umkaMakeStr(umka, value) : NULL;

    int status = 1;
    if (ushim_make_slot_from_bytes(keyType, key, keySize, &keySlot) == 0)
        status = umkaSetMapItem(umka, (UmkaMap *)map, keySlot, valueSlot) ? 0 : 1;

    if (valueSlot.ptrVal)
        umkaDecRef(umka, valueSlot.ptrVal);

    return status;
}

static int ushim_map_set_string_key_string_value_item(
    Umka *umka,
    Map *map,
    const char *key,
    const char *value)
{
    UmkaStackSlot keySlot = {0};
    UmkaStackSlot valueSlot = {0};
    keySlot.ptrVal = key ? umkaMakeStr(umka, key) : NULL;
    valueSlot.ptrVal = value ? umkaMakeStr(umka, value) : NULL;

    int status = umkaSetMapItem(umka, (UmkaMap *)map, keySlot, valueSlot) ? 0 : 1;

    if (keySlot.ptrVal)
        umkaDecRef(umka, keySlot.ptrVal);
    if (valueSlot.ptrVal)
        umkaDecRef(umka, valueSlot.ptrVal);

    return status;
}

static int ushim_set_map_bytes(
    Umka *umka,
    Map *map,
    const Type *type,
    const void *keys,
    int keyBytes,
    const void *values,
    int valueBytes)
{
    const Type *keyType = ushim_type_map_key_type(type);
    const Type *valueType = ushim_type_map_value_type(type);
    const int keySize = ushim_type_size(keyType);
    const int valueSize = ushim_type_size(valueType);
    int keyCount = 0;
    int valueCount = 0;
    if (!umka
        || !map
        || !keyType
        || !valueType
        || !ushim_map_type_accepts_bytes(type, keySize, valueSize)
        || ushim_count_from_bytes(keyBytes, keySize, &keyCount) != 0
        || ushim_count_from_bytes(valueBytes, valueSize, &valueCount) != 0
        || keyCount != valueCount
        || (!keys && keyCount > 0)
        || (!values && valueCount > 0)
        || !umkaMakeMap(umka, (UmkaMap *)map, (const UmkaType *)type))
    {
        return 1;
    }

    const char *key = (const char *)keys;
    const char *value = (const char *)values;
    for (int i = 0; i < keyCount; i++)
    {
        if (ushim_map_set_bytes_item(umka, map, type, key, value) != 0)
            return 1;

        key += keySize;
        value += valueSize;
    }

    return 0;
}

static int ushim_set_map_string_key_bytes_value(
    Umka *umka,
    Map *map,
    const Type *type,
    const char **keys,
    int keyCount,
    const void *values,
    int valueBytes)
{
    const Type *valueType = ushim_type_map_value_type(type);
    const int valueSize = ushim_type_size(valueType);
    int valueCount = 0;
    if (!umka
        || !map
        || !valueType
        || !ushim_map_type_accepts_string_key_bytes_value(type, valueSize)
        || keyCount < 0
        || ushim_count_from_bytes(valueBytes, valueSize, &valueCount) != 0
        || keyCount != valueCount
        || (!keys && keyCount > 0)
        || (!values && valueCount > 0)
        || !umkaMakeMap(umka, (UmkaMap *)map, (const UmkaType *)type))
    {
        return 1;
    }

    const char *value = (const char *)values;
    for (int i = 0; i < keyCount; i++)
    {
        if (ushim_map_set_string_key_bytes_value_item(umka, map, type, keys[i], value) != 0)
            return 1;

        value += valueSize;
    }

    return 0;
}

static int ushim_set_map_bytes_key_string_value(
    Umka *umka,
    Map *map,
    const Type *type,
    const void *keys,
    int keyBytes,
    const char **values,
    int valueCount)
{
    const Type *keyType = ushim_type_map_key_type(type);
    const int keySize = ushim_type_size(keyType);
    int keyCount = 0;
    if (!umka
        || !map
        || !keyType
        || !ushim_map_type_accepts_bytes_key_string_value(type, keySize)
        || valueCount < 0
        || ushim_count_from_bytes(keyBytes, keySize, &keyCount) != 0
        || keyCount != valueCount
        || (!keys && keyCount > 0)
        || (!values && valueCount > 0)
        || !umkaMakeMap(umka, (UmkaMap *)map, (const UmkaType *)type))
    {
        return 1;
    }

    const char *key = (const char *)keys;
    for (int i = 0; i < valueCount; i++)
    {
        if (ushim_map_set_bytes_key_string_value_item(umka, map, type, key, values[i]) != 0)
            return 1;

        key += keySize;
    }

    return 0;
}

static int ushim_set_map_string_key_string_value(
    Umka *umka,
    Map *map,
    const Type *type,
    const char **keys,
    int keyCount,
    const char **values,
    int valueCount)
{
    if (!umka
        || !map
        || !ushim_map_type_accepts_string_key_string_value(type)
        || keyCount < 0
        || valueCount < 0
        || keyCount != valueCount
        || (!keys && keyCount > 0)
        || (!values && valueCount > 0)
        || !umkaMakeMap(umka, (UmkaMap *)map, (const UmkaType *)type))
    {
        return 1;
    }

    for (int i = 0; i < keyCount; i++)
    {
        if (ushim_map_set_string_key_string_value_item(umka, map, keys[i], values[i]) != 0)
            return 1;
    }

    return 0;
}

static DynArray *ushim_dynarray_nested_item(DynArray *outer, int index)
{
    const int len = outer ? umkaGetDynArrayLen(outer) : -1;
    if (!outer || index < 0 || index >= len)
        return NULL;

    DynArray *items = (DynArray *)outer->data;
    return items ? &items[index] : NULL;
}

typedef struct
{
    UmkaFuncContext *function;
    int index;
    const void *value;
    int length;
    int elementSize;
} SetDynArrayArgData;

static int ushim_context_set_arg_dynarray_body(Umka *umka, void *data)
{
    SetDynArrayArgData *arg = (SetDynArrayArgData *)data;
    UmkaStackSlot *slot = arg->function ? ushim_param(arg->function->params, arg->index) : NULL;
    const Type *type = ushim_context_get_parameter_type(arg->function, arg->index);
    if (!slot || !ushim_dynarray_type_accepts_bytes(type, arg->elementSize) || arg->length < 0 || (!arg->value && arg->length > 0))
        return 1;

    DynArray *array = (DynArray *)slot;
    umkaMakeDynArray(umka, array, (const UmkaType *)type, arg->length);

    const int64_t bytes = (int64_t)arg->length * arg->elementSize;
    if (bytes > INT_MAX)
        return 1;

    if (bytes > 0)
        memcpy(array->data, arg->value, (size_t)bytes);

    return 0;
}

USHIM_EXPORT int ushim_context_set_arg_dynarray(
    Umka *umka,
    UmkaFuncContext *function,
    int index,
    const void *value,
    int length,
    int elementSize)
{
    SetDynArrayArgData data = {function, index, value, length, elementSize};
    return ushim_try(umka, ushim_context_set_arg_dynarray_body, &data);
}

typedef struct
{
    UmkaFuncContext *function;
    int index;
    const char **values;
    int length;
} SetDynArrayStringArgData;

static int ushim_context_set_arg_dynarray_strings_body(Umka *umka, void *data)
{
    SetDynArrayStringArgData *arg = (SetDynArrayStringArgData *)data;
    UmkaStackSlot *slot = arg->function ? ushim_param(arg->function->params, arg->index) : NULL;
    const Type *type = ushim_context_get_parameter_type(arg->function, arg->index);
    if (!umka
        || !slot
        || !ushim_dynarray_type_accepts_strings(type)
        || arg->length < 0
        || (!arg->values && arg->length > 0))
    {
        return 1;
    }

    DynArray *array = (DynArray *)slot;
    umkaMakeDynArray(umka, array, (const UmkaType *)type, arg->length);

    char **items = (char **)array->data;
    for (int i = 0; i < arg->length; i++)
        items[i] = arg->values[i] ? umkaMakeStr(umka, arg->values[i]) : NULL;

    return 0;
}

USHIM_EXPORT int ushim_context_set_arg_dynarray_strings(
    Umka *umka,
    UmkaFuncContext *function,
    int index,
    const char **values,
    int length)
{
    SetDynArrayStringArgData data = {function, index, values, length};
    return ushim_try(umka, ushim_context_set_arg_dynarray_strings_body, &data);
}

static int ushim_nested_dynarray_total_byte_count(
    const int *lengths,
    int lengthCount,
    int elementSize,
    int expectedByteCount,
    int *totalByteCount)
{
    if (!totalByteCount)
        return 1;

    *totalByteCount = 0;
    if (lengthCount < 0 || elementSize <= 0 || expectedByteCount < 0 || (!lengths && lengthCount > 0))
        return 1;

    int64_t total = 0;
    for (int i = 0; i < lengthCount; i++)
    {
        if (lengths[i] < 0)
            return 1;

        total += (int64_t)lengths[i] * elementSize;
        if (total > INT_MAX)
            return 1;
    }

    if (total != expectedByteCount)
        return 1;

    *totalByteCount = (int)total;
    return 0;
}

static int ushim_nested_dynarray_total_item_count(
    const int *lengths,
    int lengthCount,
    int expectedItemCount,
    int *totalItemCount)
{
    if (!totalItemCount)
        return 1;

    *totalItemCount = 0;
    if (lengthCount < 0 || expectedItemCount < 0 || (!lengths && lengthCount > 0))
        return 1;

    int64_t total = 0;
    for (int i = 0; i < lengthCount; i++)
    {
        if (lengths[i] < 0)
            return 1;

        total += lengths[i];
        if (total > INT_MAX)
            return 1;
    }

    if (total != expectedItemCount)
        return 1;

    *totalItemCount = (int)total;
    return 0;
}

static int ushim_set_nested_dynarray_bytes(
    Umka *umka,
    DynArray *outer,
    const Type *type,
    const int *lengths,
    int lengthCount,
    const void *values,
    int valueByteCount,
    int elementSize)
{
    int totalByteCount = 0;
    if (!umka
        || !outer
        || !ushim_dynarray_type_accepts_nested_bytes(type, elementSize)
        || ushim_nested_dynarray_total_byte_count(lengths, lengthCount, elementSize, valueByteCount, &totalByteCount) != 0
        || (!values && totalByteCount > 0))
    {
        return 1;
    }

    const Type *innerType = ushim_type_element_type(type);
    umkaMakeDynArray(umka, outer, (const UmkaType *)type, lengthCount);
    if (lengthCount > 0)
        memset(outer->data, 0, (size_t)lengthCount * sizeof(DynArray));

    const char *src = (const char *)values;
    DynArray *rows = (DynArray *)outer->data;
    for (int i = 0; i < lengthCount; i++)
    {
        const int rowLength = lengths[i];
        const int64_t rowBytes = (int64_t)rowLength * elementSize;
        if (rowBytes > INT_MAX)
            return 1;

        umkaMakeDynArray(umka, &rows[i], (const UmkaType *)innerType, rowLength);
        if (rowBytes > 0)
        {
            memcpy(rows[i].data, src, (size_t)rowBytes);
            src += rowBytes;
        }
    }

    return 0;
}

static int ushim_set_nested_dynarray_strings(
    Umka *umka,
    DynArray *outer,
    const Type *type,
    const int *lengths,
    int lengthCount,
    const char **values,
    int valueCount)
{
    int totalItemCount = 0;
    if (!umka
        || !outer
        || !ushim_dynarray_type_accepts_nested_strings(type)
        || ushim_nested_dynarray_total_item_count(lengths, lengthCount, valueCount, &totalItemCount) != 0
        || (!values && totalItemCount > 0))
    {
        return 1;
    }

    const Type *innerType = ushim_type_element_type(type);
    umkaMakeDynArray(umka, outer, (const UmkaType *)type, lengthCount);
    if (lengthCount > 0)
        memset(outer->data, 0, (size_t)lengthCount * sizeof(DynArray));

    int offset = 0;
    DynArray *rows = (DynArray *)outer->data;
    for (int i = 0; i < lengthCount; i++)
    {
        const int rowLength = lengths[i];
        umkaMakeDynArray(umka, &rows[i], (const UmkaType *)innerType, rowLength);

        char **items = (char **)rows[i].data;
        if (rowLength > 0)
            memset(items, 0, (size_t)rowLength * sizeof(char *));

        for (int j = 0; j < rowLength; j++)
            items[j] = values[offset + j] ? umkaMakeStr(umka, values[offset + j]) : NULL;

        offset += rowLength;
    }

    return 0;
}

typedef struct
{
    UmkaFuncContext *function;
    int index;
    const int *lengths;
    int lengthCount;
    const void *values;
    int valueByteCount;
    int elementSize;
} SetNestedDynArrayArgData;

static int ushim_context_set_arg_nested_dynarray_body(Umka *umka, void *data)
{
    SetNestedDynArrayArgData *arg = (SetNestedDynArrayArgData *)data;
    UmkaStackSlot *slot = arg->function ? ushim_param(arg->function->params, arg->index) : NULL;
    const Type *type = ushim_context_get_parameter_type(arg->function, arg->index);
    return ushim_set_nested_dynarray_bytes(
        umka,
        (DynArray *)slot,
        type,
        arg->lengths,
        arg->lengthCount,
        arg->values,
        arg->valueByteCount,
        arg->elementSize);
}

USHIM_EXPORT int ushim_context_set_arg_nested_dynarray(
    Umka *umka,
    UmkaFuncContext *function,
    int index,
    const int *lengths,
    int lengthCount,
    const void *values,
    int valueByteCount,
    int elementSize)
{
    SetNestedDynArrayArgData data = {function, index, lengths, lengthCount, values, valueByteCount, elementSize};
    return ushim_try(umka, ushim_context_set_arg_nested_dynarray_body, &data);
}

typedef struct
{
    UmkaFuncContext *function;
    int index;
    const int *lengths;
    int lengthCount;
    const char **values;
    int valueCount;
} SetNestedDynArrayStringArgData;

static int ushim_context_set_arg_nested_dynarray_strings_body(Umka *umka, void *data)
{
    SetNestedDynArrayStringArgData *arg = (SetNestedDynArrayStringArgData *)data;
    UmkaStackSlot *slot = arg->function ? ushim_param(arg->function->params, arg->index) : NULL;
    const Type *type = ushim_context_get_parameter_type(arg->function, arg->index);
    return ushim_set_nested_dynarray_strings(
        umka,
        (DynArray *)slot,
        type,
        arg->lengths,
        arg->lengthCount,
        arg->values,
        arg->valueCount);
}

USHIM_EXPORT int ushim_context_set_arg_nested_dynarray_strings(
    Umka *umka,
    UmkaFuncContext *function,
    int index,
    const int *lengths,
    int lengthCount,
    const char **values,
    int valueCount)
{
    SetNestedDynArrayStringArgData data = {function, index, lengths, lengthCount, values, valueCount};
    return ushim_try(umka, ushim_context_set_arg_nested_dynarray_strings_body, &data);
}

typedef struct
{
    UmkaFuncContext *function;
    int index;
    const void *keys;
    int keyBytes;
    const void *values;
    int valueBytes;
} SetMapArgData;

static int ushim_context_set_arg_map_body(Umka *umka, void *data)
{
    SetMapArgData *arg = (SetMapArgData *)data;
    UmkaStackSlot *slot = arg->function ? ushim_param(arg->function->params, arg->index) : NULL;
    const Type *type = ushim_context_get_parameter_type(arg->function, arg->index);
    return ushim_set_map_bytes(
        umka,
        (Map *)slot,
        type,
        arg->keys,
        arg->keyBytes,
        arg->values,
        arg->valueBytes);
}

USHIM_EXPORT int ushim_context_set_arg_map(
    Umka *umka,
    UmkaFuncContext *function,
    int index,
    const void *keys,
    int keyBytes,
    const void *values,
    int valueBytes)
{
    SetMapArgData data = {function, index, keys, keyBytes, values, valueBytes};
    return ushim_try(umka, ushim_context_set_arg_map_body, &data);
}

typedef struct
{
    UmkaFuncContext *function;
    int index;
    const char **keys;
    int keyCount;
    const void *values;
    int valueBytes;
} SetStringKeyMapArgData;

static int ushim_context_set_arg_string_key_map_body(Umka *umka, void *data)
{
    SetStringKeyMapArgData *arg = (SetStringKeyMapArgData *)data;
    UmkaStackSlot *slot = arg->function ? ushim_param(arg->function->params, arg->index) : NULL;
    const Type *type = ushim_context_get_parameter_type(arg->function, arg->index);
    return ushim_set_map_string_key_bytes_value(
        umka,
        (Map *)slot,
        type,
        arg->keys,
        arg->keyCount,
        arg->values,
        arg->valueBytes);
}

USHIM_EXPORT int ushim_context_set_arg_string_key_map(
    Umka *umka,
    UmkaFuncContext *function,
    int index,
    const char **keys,
    int keyCount,
    const void *values,
    int valueBytes)
{
    SetStringKeyMapArgData data = {function, index, keys, keyCount, values, valueBytes};
    return ushim_try(umka, ushim_context_set_arg_string_key_map_body, &data);
}

typedef struct
{
    UmkaFuncContext *function;
    int index;
    const void *keys;
    int keyBytes;
    const char **values;
    int valueCount;
} SetStringValueMapArgData;

static int ushim_context_set_arg_string_value_map_body(Umka *umka, void *data)
{
    SetStringValueMapArgData *arg = (SetStringValueMapArgData *)data;
    UmkaStackSlot *slot = arg->function ? ushim_param(arg->function->params, arg->index) : NULL;
    const Type *type = ushim_context_get_parameter_type(arg->function, arg->index);
    return ushim_set_map_bytes_key_string_value(
        umka,
        (Map *)slot,
        type,
        arg->keys,
        arg->keyBytes,
        arg->values,
        arg->valueCount);
}

USHIM_EXPORT int ushim_context_set_arg_string_value_map(
    Umka *umka,
    UmkaFuncContext *function,
    int index,
    const void *keys,
    int keyBytes,
    const char **values,
    int valueCount)
{
    SetStringValueMapArgData data = {function, index, keys, keyBytes, values, valueCount};
    return ushim_try(umka, ushim_context_set_arg_string_value_map_body, &data);
}

typedef struct
{
    UmkaFuncContext *function;
    int index;
    const char **keys;
    int keyCount;
    const char **values;
    int valueCount;
} SetStringMapArgData;

static int ushim_context_set_arg_string_map_body(Umka *umka, void *data)
{
    SetStringMapArgData *arg = (SetStringMapArgData *)data;
    UmkaStackSlot *slot = arg->function ? ushim_param(arg->function->params, arg->index) : NULL;
    const Type *type = ushim_context_get_parameter_type(arg->function, arg->index);
    return ushim_set_map_string_key_string_value(
        umka,
        (Map *)slot,
        type,
        arg->keys,
        arg->keyCount,
        arg->values,
        arg->valueCount);
}

USHIM_EXPORT int ushim_context_set_arg_string_map(
    Umka *umka,
    UmkaFuncContext *function,
    int index,
    const char **keys,
    int keyCount,
    const char **values,
    int valueCount)
{
    SetStringMapArgData data = {function, index, keys, keyCount, values, valueCount};
    return ushim_try(umka, ushim_context_set_arg_string_map_body, &data);
}

static DynArray *ushim_context_result_dynarray(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_result_type(function);
    UmkaStackSlot *slot = ushim_result(function);
    if (ushim_type_kind(type) != TYPE_DYNARRAY || !slot || !slot->ptrVal)
        return NULL;

    return (DynArray *)slot->ptrVal;
}

USHIM_EXPORT int ushim_context_get_result_dynarray_length(UmkaFuncContext *function)
{
    DynArray *array = ushim_context_result_dynarray(function);
    return array ? umkaGetDynArrayLen(array) : -1;
}

USHIM_EXPORT int ushim_context_copy_result_dynarray_data(UmkaFuncContext *function, void *buffer, int size)
{
    DynArray *array = ushim_context_result_dynarray(function);
    const Type *type = ushim_context_get_result_type(function);
    int byteCount = 0;
    if (!array
        || !type
        || ushim_type_kind(type) != TYPE_DYNARRAY
        || ushim_type_element_has_references(type)
        || ushim_dynarray_byte_count(array, &byteCount) != 0
        || size != byteCount
        || (!buffer && byteCount > 0))
    {
        return 1;
    }

    if (byteCount > 0)
        memcpy(buffer, array->data, (size_t)byteCount);

    return 0;
}

USHIM_EXPORT int ushim_context_get_result_dynarray_string(UmkaFuncContext *function, int index, const char **value)
{
    DynArray *array = ushim_context_result_dynarray(function);
    const Type *type = ushim_context_get_result_type(function);
    const int len = array ? umkaGetDynArrayLen(array) : -1;
    if (!value
        || !array
        || !ushim_dynarray_type_accepts_strings(type)
        || index < 0
        || index >= len)
    {
        return 1;
    }

    const char **items = (const char **)array->data;
    *value = items[index];
    return 0;
}

USHIM_EXPORT int ushim_context_get_result_nested_dynarray_length(UmkaFuncContext *function, int index)
{
    DynArray *outer = ushim_context_result_dynarray(function);
    const Type *type = ushim_context_get_result_type(function);
    if (!outer || !ushim_dynarray_type_accepts_nested_bytes(type, ushim_type_nested_dynarray_element_size(type)))
        return -1;

    DynArray *inner = ushim_dynarray_nested_item(outer, index);
    return inner ? umkaGetDynArrayLen(inner) : -1;
}

USHIM_EXPORT int ushim_context_copy_result_nested_dynarray_data(
    UmkaFuncContext *function,
    int index,
    void *buffer,
    int size,
    int elementSize)
{
    DynArray *outer = ushim_context_result_dynarray(function);
    const Type *type = ushim_context_get_result_type(function);
    DynArray *inner = ushim_dynarray_nested_item(outer, index);
    int byteCount = 0;
    if (!outer
        || !inner
        || !ushim_dynarray_type_accepts_nested_bytes(type, elementSize)
        || inner->itemSize != elementSize
        || ushim_dynarray_byte_count(inner, &byteCount) != 0
        || size != byteCount
        || (!buffer && byteCount > 0))
    {
        return 1;
    }

    if (byteCount > 0)
        memcpy(buffer, inner->data, (size_t)byteCount);

    return 0;
}

USHIM_EXPORT int ushim_context_get_result_nested_string_array_length(UmkaFuncContext *function, int index)
{
    DynArray *outer = ushim_context_result_dynarray(function);
    const Type *type = ushim_context_get_result_type(function);
    if (!outer || !ushim_dynarray_type_accepts_nested_strings(type))
        return -1;

    DynArray *inner = ushim_dynarray_nested_item(outer, index);
    return inner ? umkaGetDynArrayLen(inner) : -1;
}

USHIM_EXPORT int ushim_context_copy_result_nested_string_array_data(
    UmkaFuncContext *function,
    int index,
    const char **values,
    int valueCount)
{
    DynArray *outer = ushim_context_result_dynarray(function);
    const Type *type = ushim_context_get_result_type(function);
    DynArray *inner = ushim_dynarray_nested_item(outer, index);
    const int len = inner ? umkaGetDynArrayLen(inner) : -1;
    if (!outer
        || !inner
        || !ushim_dynarray_type_accepts_nested_strings(type)
        || inner->itemSize != (int)sizeof(char *)
        || len < 0
        || valueCount != len
        || (!values && len > 0))
    {
        return 1;
    }

    if (len > 0)
        memcpy(values, inner->data, (size_t)len * sizeof(char *));

    return 0;
}

typedef struct
{
    UmkaFuncContext *function;
} ReleaseContextDynArrayData;

static int ushim_context_release_result_dynarray_body(Umka *umka, void *data)
{
    ReleaseContextDynArrayData *arg = (ReleaseContextDynArrayData *)data;
    DynArray *array = ushim_context_result_dynarray(arg->function);
    if (!umka || !array)
        return 0;

    if (array->data)
        umkaDecRef(umka, array->data);

    memset(array, 0, sizeof(DynArray));
    return 0;
}

USHIM_EXPORT int ushim_context_release_result_dynarray(Umka *umka, UmkaFuncContext *function)
{
    ReleaseContextDynArrayData data = {function};
    return ushim_try(umka, ushim_context_release_result_dynarray_body, &data);
}

static int ushim_map_count(const Map *map)
{
    if (!map)
        return -1;

    if (!map->root)
        return 0;

    if (map->root->len < 0 || map->root->len > INT_MAX)
        return -1;

    return (int)map->root->len;
}

static int ushim_map_type_accepts_copy(const Type *type, int keySize, int valueSize)
{
    return type
        && ushim_type_kind(type) == TYPE_MAP
        && !ushim_type_map_key_has_references(type)
        && !ushim_type_map_value_has_references(type)
        && ushim_type_map_key_size(type) > 0
        && ushim_type_map_value_size(type) > 0
        && keySize == ushim_type_map_key_size(type)
        && valueSize == ushim_type_map_value_size(type);
}

static int ushim_map_type_accepts_string_key_copy(const Type *type, int valueSize)
{
    return type
        && ushim_type_kind(type) == TYPE_MAP
        && ushim_type_map_key_kind(type) == TYPE_STR
        && !ushim_type_map_value_has_references(type)
        && ushim_type_map_key_size(type) == (int)sizeof(char *)
        && ushim_type_map_value_size(type) > 0
        && valueSize == ushim_type_map_value_size(type);
}

static int ushim_map_type_accepts_string_value_copy(const Type *type, int keySize)
{
    return type
        && ushim_type_kind(type) == TYPE_MAP
        && !ushim_type_map_key_has_references(type)
        && ushim_type_map_value_kind(type) == TYPE_STR
        && ushim_type_map_key_size(type) > 0
        && ushim_type_map_value_size(type) == (int)sizeof(char *)
        && keySize == ushim_type_map_key_size(type);
}

static int ushim_map_type_accepts_string_copy(const Type *type)
{
    return type
        && ushim_type_kind(type) == TYPE_MAP
        && ushim_type_map_key_kind(type) == TYPE_STR
        && ushim_type_map_value_kind(type) == TYPE_STR
        && ushim_type_map_key_size(type) == (int)sizeof(char *)
        && ushim_type_map_value_size(type) == (int)sizeof(char *);
}

static int ushim_map_type_accepts_dynarray_value_copy(const Type *type, int keySize, int elementSize)
{
    const Type *valueType = ushim_type_map_value_type(type);
    const Type *elementType = ushim_type_element_type(valueType);
    return type
        && ushim_type_kind(type) == TYPE_MAP
        && !ushim_type_map_key_has_references(type)
        && valueType
        && ushim_type_kind(valueType) == TYPE_DYNARRAY
        && ushim_type_size(valueType) == (int)sizeof(DynArray)
        && elementType
        && !ushim_type_has_references(elementType)
        && ushim_type_map_key_size(type) > 0
        && keySize == ushim_type_map_key_size(type)
        && elementSize > 0
        && elementSize == ushim_type_size(elementType);
}

static int ushim_map_type_accepts_string_key_dynarray_value_copy(const Type *type, int elementSize)
{
    const Type *valueType = ushim_type_map_value_type(type);
    const Type *elementType = ushim_type_element_type(valueType);
    return type
        && ushim_type_kind(type) == TYPE_MAP
        && ushim_type_map_key_kind(type) == TYPE_STR
        && ushim_type_map_key_size(type) == (int)sizeof(char *)
        && valueType
        && ushim_type_kind(valueType) == TYPE_DYNARRAY
        && ushim_type_size(valueType) == (int)sizeof(DynArray)
        && elementType
        && !ushim_type_has_references(elementType)
        && elementSize > 0
        && elementSize == ushim_type_size(elementType);
}

static int ushim_map_type_accepts_string_array_value_copy(const Type *type, int keySize)
{
    const Type *valueType = ushim_type_map_value_type(type);
    return type
        && ushim_type_kind(type) == TYPE_MAP
        && !ushim_type_map_key_has_references(type)
        && valueType
        && ushim_type_size(valueType) == (int)sizeof(DynArray)
        && ushim_dynarray_type_accepts_strings(valueType)
        && ushim_type_map_key_size(type) > 0
        && keySize == ushim_type_map_key_size(type);
}

static int ushim_map_type_accepts_string_key_string_array_value_copy(const Type *type)
{
    const Type *valueType = ushim_type_map_value_type(type);
    return type
        && ushim_type_kind(type) == TYPE_MAP
        && ushim_type_map_key_kind(type) == TYPE_STR
        && ushim_type_map_key_size(type) == (int)sizeof(char *)
        && valueType
        && ushim_type_size(valueType) == (int)sizeof(DynArray)
        && ushim_dynarray_type_accepts_strings(valueType);
}

static const char *ushim_map_string_item(const void *item)
{
    return item ? *(const char * const *)item : NULL;
}

static void ushim_copy_map_entries_recursively(
    const MapNode *node,
    char *keys,
    int keySize,
    char *values,
    int valueSize,
    int *index)
{
    if (!node)
        return;

    ushim_copy_map_entries_recursively(node->left, keys, keySize, values, valueSize, index);

    if (node->key && node->data)
    {
        memcpy(keys + (int64_t)(*index) * keySize, node->key, (size_t)keySize);
        memcpy(values + (int64_t)(*index) * valueSize, node->data, (size_t)valueSize);
        (*index)++;
    }

    ushim_copy_map_entries_recursively(node->right, keys, keySize, values, valueSize, index);
}

static void ushim_copy_map_string_key_entries_recursively(
    const MapNode *node,
    const char **keys,
    char *values,
    int valueSize,
    int *index)
{
    if (!node)
        return;

    ushim_copy_map_string_key_entries_recursively(node->left, keys, values, valueSize, index);

    if (node->key && node->data)
    {
        keys[*index] = ushim_map_string_item(node->key);
        memcpy(values + (int64_t)(*index) * valueSize, node->data, (size_t)valueSize);
        (*index)++;
    }

    ushim_copy_map_string_key_entries_recursively(node->right, keys, values, valueSize, index);
}

static void ushim_copy_map_string_value_entries_recursively(
    const MapNode *node,
    char *keys,
    int keySize,
    const char **values,
    int *index)
{
    if (!node)
        return;

    ushim_copy_map_string_value_entries_recursively(node->left, keys, keySize, values, index);

    if (node->key && node->data)
    {
        memcpy(keys + (int64_t)(*index) * keySize, node->key, (size_t)keySize);
        values[*index] = ushim_map_string_item(node->data);
        (*index)++;
    }

    ushim_copy_map_string_value_entries_recursively(node->right, keys, keySize, values, index);
}

static void ushim_copy_map_string_entries_recursively(
    const MapNode *node,
    const char **keys,
    const char **values,
    int *index)
{
    if (!node)
        return;

    ushim_copy_map_string_entries_recursively(node->left, keys, values, index);

    if (node->key && node->data)
    {
        keys[*index] = ushim_map_string_item(node->key);
        values[*index] = ushim_map_string_item(node->data);
        (*index)++;
    }

    ushim_copy_map_string_entries_recursively(node->right, keys, values, index);
}

static void ushim_copy_map_dynarray_value_entries_recursively(
    const MapNode *node,
    char *keys,
    int keySize,
    int *lengths,
    int *index,
    int *failed)
{
    if (!node || *failed)
        return;

    ushim_copy_map_dynarray_value_entries_recursively(node->left, keys, keySize, lengths, index, failed);

    if (*failed)
        return;

    if (node->key && node->data)
    {
        DynArray *value = (DynArray *)node->data;
        const int len = umkaGetDynArrayLen(value);
        if (len < 0)
        {
            *failed = 1;
            return;
        }

        memcpy(keys + (int64_t)(*index) * keySize, node->key, (size_t)keySize);
        lengths[*index] = len;
        (*index)++;
    }

    ushim_copy_map_dynarray_value_entries_recursively(node->right, keys, keySize, lengths, index, failed);
}

static void ushim_copy_map_string_key_dynarray_value_entries_recursively(
    const MapNode *node,
    const char **keys,
    int *lengths,
    int *index,
    int *failed)
{
    if (!node || *failed)
        return;

    ushim_copy_map_string_key_dynarray_value_entries_recursively(node->left, keys, lengths, index, failed);

    if (*failed)
        return;

    if (node->key && node->data)
    {
        DynArray *value = (DynArray *)node->data;
        const int len = umkaGetDynArrayLen(value);
        if (len < 0)
        {
            *failed = 1;
            return;
        }

        keys[*index] = ushim_map_string_item(node->key);
        lengths[*index] = len;
        (*index)++;
    }

    ushim_copy_map_string_key_dynarray_value_entries_recursively(node->right, keys, lengths, index, failed);
}

static DynArray *ushim_map_dynarray_value_at_recursively(const MapNode *node, int target, int *index)
{
    if (!node)
        return NULL;

    DynArray *left = ushim_map_dynarray_value_at_recursively(node->left, target, index);
    if (left)
        return left;

    if (node->key && node->data)
    {
        if (*index == target)
            return (DynArray *)node->data;

        (*index)++;
    }

    return ushim_map_dynarray_value_at_recursively(node->right, target, index);
}

static int ushim_copy_map_entries(
    const Map *map,
    const Type *type,
    void *keys,
    int keyBytes,
    void *values,
    int valueBytes)
{
    const int count = ushim_map_count(map);
    const int keySize = ushim_type_map_key_size(type);
    const int valueSize = ushim_type_map_value_size(type);
    const int64_t expectedKeyBytes = (int64_t)count * keySize;
    const int64_t expectedValueBytes = (int64_t)count * valueSize;
    if (count < 0
        || !ushim_map_type_accepts_copy(type, keySize, valueSize)
        || expectedKeyBytes > INT_MAX
        || expectedValueBytes > INT_MAX
        || keyBytes != expectedKeyBytes
        || valueBytes != expectedValueBytes
        || (!keys && keyBytes > 0)
        || (!values && valueBytes > 0))
    {
        return 1;
    }

    if (count == 0)
        return 0;

    int index = 0;
    ushim_copy_map_entries_recursively(map->root, (char *)keys, keySize, (char *)values, valueSize, &index);
    return index == count ? 0 : 1;
}

static int ushim_copy_map_string_key_entries(
    const Map *map,
    const Type *type,
    const char **keys,
    int keyCount,
    void *values,
    int valueBytes)
{
    const int count = ushim_map_count(map);
    const int valueSize = ushim_type_map_value_size(type);
    const int64_t expectedValueBytes = (int64_t)count * valueSize;
    if (count < 0
        || !ushim_map_type_accepts_string_key_copy(type, valueSize)
        || expectedValueBytes > INT_MAX
        || keyCount != count
        || valueBytes != expectedValueBytes
        || (!keys && count > 0)
        || (!values && valueBytes > 0))
    {
        return 1;
    }

    if (count == 0)
        return 0;

    int index = 0;
    ushim_copy_map_string_key_entries_recursively(map->root, keys, (char *)values, valueSize, &index);
    return index == count ? 0 : 1;
}

static int ushim_copy_map_string_value_entries(
    const Map *map,
    const Type *type,
    void *keys,
    int keyBytes,
    const char **values,
    int valueCount)
{
    const int count = ushim_map_count(map);
    const int keySize = ushim_type_map_key_size(type);
    const int64_t expectedKeyBytes = (int64_t)count * keySize;
    if (count < 0
        || !ushim_map_type_accepts_string_value_copy(type, keySize)
        || expectedKeyBytes > INT_MAX
        || keyBytes != expectedKeyBytes
        || valueCount != count
        || (!keys && keyBytes > 0)
        || (!values && count > 0))
    {
        return 1;
    }

    if (count == 0)
        return 0;

    int index = 0;
    ushim_copy_map_string_value_entries_recursively(map->root, (char *)keys, keySize, values, &index);
    return index == count ? 0 : 1;
}

static int ushim_copy_map_string_entries(
    const Map *map,
    const Type *type,
    const char **keys,
    int keyCount,
    const char **values,
    int valueCount)
{
    const int count = ushim_map_count(map);
    if (count < 0
        || !ushim_map_type_accepts_string_copy(type)
        || keyCount != count
        || valueCount != count
        || (!keys && count > 0)
        || (!values && count > 0))
    {
        return 1;
    }

    if (count == 0)
        return 0;

    int index = 0;
    ushim_copy_map_string_entries_recursively(map->root, keys, values, &index);
    return index == count ? 0 : 1;
}

static int ushim_copy_map_dynarray_value_entries(
    const Map *map,
    const Type *type,
    void *keys,
    int keyBytes,
    int *lengths,
    int lengthCount,
    int elementSize)
{
    const int count = ushim_map_count(map);
    const int keySize = ushim_type_map_key_size(type);
    const int64_t expectedKeyBytes = (int64_t)count * keySize;
    if (count < 0
        || !ushim_map_type_accepts_dynarray_value_copy(type, keySize, elementSize)
        || expectedKeyBytes > INT_MAX
        || keyBytes != expectedKeyBytes
        || lengthCount != count
        || (!keys && keyBytes > 0)
        || (!lengths && count > 0))
    {
        return 1;
    }

    if (count == 0)
        return 0;

    int index = 0;
    int failed = 0;
    ushim_copy_map_dynarray_value_entries_recursively(map->root, (char *)keys, keySize, lengths, &index, &failed);
    return !failed && index == count ? 0 : 1;
}

static int ushim_copy_map_string_key_dynarray_value_entries(
    const Map *map,
    const Type *type,
    const char **keys,
    int keyCount,
    int *lengths,
    int lengthCount,
    int elementSize)
{
    const int count = ushim_map_count(map);
    if (count < 0
        || !ushim_map_type_accepts_string_key_dynarray_value_copy(type, elementSize)
        || keyCount != count
        || lengthCount != count
        || (!keys && count > 0)
        || (!lengths && count > 0))
    {
        return 1;
    }

    if (count == 0)
        return 0;

    int index = 0;
    int failed = 0;
    ushim_copy_map_string_key_dynarray_value_entries_recursively(map->root, keys, lengths, &index, &failed);
    return !failed && index == count ? 0 : 1;
}

static int ushim_copy_map_string_array_value_entries(
    const Map *map,
    const Type *type,
    void *keys,
    int keyBytes,
    int *lengths,
    int lengthCount)
{
    const int count = ushim_map_count(map);
    const int keySize = ushim_type_map_key_size(type);
    const int64_t expectedKeyBytes = (int64_t)count * keySize;
    if (count < 0
        || !ushim_map_type_accepts_string_array_value_copy(type, keySize)
        || expectedKeyBytes > INT_MAX
        || keyBytes != expectedKeyBytes
        || lengthCount != count
        || (!keys && keyBytes > 0)
        || (!lengths && count > 0))
    {
        return 1;
    }

    if (count == 0)
        return 0;

    int index = 0;
    int failed = 0;
    ushim_copy_map_dynarray_value_entries_recursively(map->root, (char *)keys, keySize, lengths, &index, &failed);
    return !failed && index == count ? 0 : 1;
}

static int ushim_copy_map_string_key_string_array_value_entries(
    const Map *map,
    const Type *type,
    const char **keys,
    int keyCount,
    int *lengths,
    int lengthCount)
{
    const int count = ushim_map_count(map);
    if (count < 0
        || !ushim_map_type_accepts_string_key_string_array_value_copy(type)
        || keyCount != count
        || lengthCount != count
        || (!keys && count > 0)
        || (!lengths && count > 0))
    {
        return 1;
    }

    if (count == 0)
        return 0;

    int index = 0;
    int failed = 0;
    ushim_copy_map_string_key_dynarray_value_entries_recursively(map->root, keys, lengths, &index, &failed);
    return !failed && index == count ? 0 : 1;
}

static int ushim_copy_map_dynarray_value_data(
    const Map *map,
    const Type *type,
    int entryIndex,
    void *buffer,
    int size,
    int elementSize)
{
    const int count = ushim_map_count(map);
    int index = 0;
    int byteCount = 0;
    DynArray *value = NULL;
    if (count < 0
        || !ushim_map_type_accepts_dynarray_value_copy(type, ushim_type_map_key_size(type), elementSize)
        || entryIndex < 0
        || entryIndex >= count)
    {
        return 1;
    }

    value = ushim_map_dynarray_value_at_recursively(map->root, entryIndex, &index);
    if (!value
        || value->itemSize != elementSize
        || ushim_dynarray_byte_count(value, &byteCount) != 0
        || size != byteCount
        || (!buffer && byteCount > 0))
    {
        return 1;
    }

    if (byteCount > 0)
        memcpy(buffer, value->data, (size_t)byteCount);

    return 0;
}

static int ushim_copy_map_string_key_dynarray_value_data(
    const Map *map,
    const Type *type,
    int entryIndex,
    void *buffer,
    int size,
    int elementSize)
{
    const int count = ushim_map_count(map);
    int index = 0;
    int byteCount = 0;
    DynArray *value = NULL;
    if (count < 0
        || !ushim_map_type_accepts_string_key_dynarray_value_copy(type, elementSize)
        || entryIndex < 0
        || entryIndex >= count)
    {
        return 1;
    }

    value = ushim_map_dynarray_value_at_recursively(map->root, entryIndex, &index);
    if (!value
        || value->itemSize != elementSize
        || ushim_dynarray_byte_count(value, &byteCount) != 0
        || size != byteCount
        || (!buffer && byteCount > 0))
    {
        return 1;
    }

    if (byteCount > 0)
        memcpy(buffer, value->data, (size_t)byteCount);

    return 0;
}

static int ushim_copy_map_string_array_value_data(
    const Map *map,
    const Type *type,
    int entryIndex,
    const char **values,
    int valueCount)
{
    const int count = ushim_map_count(map);
    int index = 0;
    DynArray *value = NULL;
    if (count < 0
        || !ushim_map_type_accepts_string_array_value_copy(type, ushim_type_map_key_size(type))
        || entryIndex < 0
        || entryIndex >= count)
    {
        return 1;
    }

    value = ushim_map_dynarray_value_at_recursively(map->root, entryIndex, &index);
    const int len = value ? umkaGetDynArrayLen(value) : -1;
    if (!value
        || value->itemSize != (int)sizeof(char *)
        || len < 0
        || valueCount != len
        || (!values && len > 0))
    {
        return 1;
    }

    if (len > 0)
        memcpy(values, value->data, (size_t)len * sizeof(char *));

    return 0;
}

static int ushim_copy_map_string_key_string_array_value_data(
    const Map *map,
    const Type *type,
    int entryIndex,
    const char **values,
    int valueCount)
{
    const int count = ushim_map_count(map);
    int index = 0;
    DynArray *value = NULL;
    if (count < 0
        || !ushim_map_type_accepts_string_key_string_array_value_copy(type)
        || entryIndex < 0
        || entryIndex >= count)
    {
        return 1;
    }

    value = ushim_map_dynarray_value_at_recursively(map->root, entryIndex, &index);
    const int len = value ? umkaGetDynArrayLen(value) : -1;
    if (!value
        || value->itemSize != (int)sizeof(char *)
        || len < 0
        || valueCount != len
        || (!values && len > 0))
    {
        return 1;
    }

    if (len > 0)
        memcpy(values, value->data, (size_t)len * sizeof(char *));

    return 0;
}

static Map *ushim_context_result_map(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_result_type(function);
    UmkaStackSlot *slot = ushim_result(function);
    if (ushim_type_kind(type) != TYPE_MAP || !slot || !slot->ptrVal)
        return NULL;

    return (Map *)slot->ptrVal;
}

USHIM_EXPORT int ushim_context_get_result_map_count(UmkaFuncContext *function)
{
    return ushim_map_count(ushim_context_result_map(function));
}

USHIM_EXPORT int ushim_context_copy_result_map_entries(
    UmkaFuncContext *function,
    void *keys,
    int keyBytes,
    void *values,
    int valueBytes)
{
    return ushim_copy_map_entries(
        ushim_context_result_map(function),
        ushim_context_get_result_type(function),
        keys,
        keyBytes,
        values,
        valueBytes);
}

USHIM_EXPORT int ushim_context_copy_result_string_key_map_entries(
    UmkaFuncContext *function,
    const char **keys,
    int keyCount,
    void *values,
    int valueBytes)
{
    return ushim_copy_map_string_key_entries(
        ushim_context_result_map(function),
        ushim_context_get_result_type(function),
        keys,
        keyCount,
        values,
        valueBytes);
}

USHIM_EXPORT int ushim_context_copy_result_string_value_map_entries(
    UmkaFuncContext *function,
    void *keys,
    int keyBytes,
    const char **values,
    int valueCount)
{
    return ushim_copy_map_string_value_entries(
        ushim_context_result_map(function),
        ushim_context_get_result_type(function),
        keys,
        keyBytes,
        values,
        valueCount);
}

USHIM_EXPORT int ushim_context_copy_result_string_map_entries(
    UmkaFuncContext *function,
    const char **keys,
    int keyCount,
    const char **values,
    int valueCount)
{
    return ushim_copy_map_string_entries(
        ushim_context_result_map(function),
        ushim_context_get_result_type(function),
        keys,
        keyCount,
        values,
        valueCount);
}

USHIM_EXPORT int ushim_context_copy_result_map_dynarray_value_entries(
    UmkaFuncContext *function,
    void *keys,
    int keyBytes,
    int *lengths,
    int lengthCount,
    int elementSize)
{
    return ushim_copy_map_dynarray_value_entries(
        ushim_context_result_map(function),
        ushim_context_get_result_type(function),
        keys,
        keyBytes,
        lengths,
        lengthCount,
        elementSize);
}

USHIM_EXPORT int ushim_context_copy_result_map_dynarray_value_data(
    UmkaFuncContext *function,
    int entryIndex,
    void *buffer,
    int size,
    int elementSize)
{
    return ushim_copy_map_dynarray_value_data(
        ushim_context_result_map(function),
        ushim_context_get_result_type(function),
        entryIndex,
        buffer,
        size,
        elementSize);
}

USHIM_EXPORT int ushim_context_copy_result_map_string_array_value_entries(
    UmkaFuncContext *function,
    void *keys,
    int keyBytes,
    int *lengths,
    int lengthCount)
{
    return ushim_copy_map_string_array_value_entries(
        ushim_context_result_map(function),
        ushim_context_get_result_type(function),
        keys,
        keyBytes,
        lengths,
        lengthCount);
}

USHIM_EXPORT int ushim_context_copy_result_map_string_array_value_data(
    UmkaFuncContext *function,
    int entryIndex,
    const char **values,
    int valueCount)
{
    return ushim_copy_map_string_array_value_data(
        ushim_context_result_map(function),
        ushim_context_get_result_type(function),
        entryIndex,
        values,
        valueCount);
}

USHIM_EXPORT int ushim_context_copy_result_string_key_map_dynarray_value_entries(
    UmkaFuncContext *function,
    const char **keys,
    int keyCount,
    int *lengths,
    int lengthCount,
    int elementSize)
{
    return ushim_copy_map_string_key_dynarray_value_entries(
        ushim_context_result_map(function),
        ushim_context_get_result_type(function),
        keys,
        keyCount,
        lengths,
        lengthCount,
        elementSize);
}

USHIM_EXPORT int ushim_context_copy_result_string_key_map_dynarray_value_data(
    UmkaFuncContext *function,
    int entryIndex,
    void *buffer,
    int size,
    int elementSize)
{
    return ushim_copy_map_string_key_dynarray_value_data(
        ushim_context_result_map(function),
        ushim_context_get_result_type(function),
        entryIndex,
        buffer,
        size,
        elementSize);
}

USHIM_EXPORT int ushim_context_copy_result_string_key_map_string_array_value_entries(
    UmkaFuncContext *function,
    const char **keys,
    int keyCount,
    int *lengths,
    int lengthCount)
{
    return ushim_copy_map_string_key_string_array_value_entries(
        ushim_context_result_map(function),
        ushim_context_get_result_type(function),
        keys,
        keyCount,
        lengths,
        lengthCount);
}

USHIM_EXPORT int ushim_context_copy_result_string_key_map_string_array_value_data(
    UmkaFuncContext *function,
    int entryIndex,
    const char **values,
    int valueCount)
{
    return ushim_copy_map_string_key_string_array_value_data(
        ushim_context_result_map(function),
        ushim_context_get_result_type(function),
        entryIndex,
        values,
        valueCount);
}

USHIM_EXPORT const char *ushim_context_get_result_string(UmkaFuncContext *function)
{
    UmkaStackSlot *slot = ushim_result(function);
    return slot ? (const char *)slot->ptrVal : NULL;
}

USHIM_EXPORT int ushim_callback_get_argument_count(UmkaStackSlot *params)
{
    if (!params)
        return 0;

    int count = 0;
    while (umkaGetParamType(params, count))
        count++;
    return count;
}

static const Type *ushim_callback_get_parameter_type(UmkaStackSlot *params, int index)
{
    if (!params)
        return NULL;

    return (const Type *)umkaGetParamType(params, index);
}

static const Type *ushim_callback_get_result_type(UmkaStackSlot *params, UmkaStackSlot *result)
{
    if (!params)
        return NULL;

    return (const Type *)umkaGetResultType(params, result);
}

USHIM_EXPORT int ushim_callback_get_parameter_kind(UmkaStackSlot *params, int index)
{
    const Type *type = ushim_callback_get_parameter_type(params, index);
    return ushim_type_kind(type);
}

USHIM_EXPORT int ushim_callback_get_parameter_size(UmkaStackSlot *params, int index)
{
    const Type *type = ushim_callback_get_parameter_type(params, index);
    return ushim_type_size(type);
}

USHIM_EXPORT int ushim_callback_get_parameter_item_count(UmkaStackSlot *params, int index)
{
    const Type *type = ushim_callback_get_parameter_type(params, index);
    return ushim_type_item_count(type);
}

USHIM_EXPORT int ushim_callback_get_parameter_element_kind(UmkaStackSlot *params, int index)
{
    return ushim_type_element_kind(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_element_size(UmkaStackSlot *params, int index)
{
    return ushim_type_element_size(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_element_has_references(UmkaStackSlot *params, int index)
{
    return ushim_type_element_has_references(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT const char *ushim_callback_get_parameter_element_type_name(UmkaStackSlot *params, int index)
{
    return ushim_type_element_name(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_nested_element_kind(UmkaStackSlot *params, int index)
{
    return ushim_type_nested_dynarray_element_kind(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_nested_element_size(UmkaStackSlot *params, int index)
{
    return ushim_type_nested_dynarray_element_size(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_nested_element_has_references(UmkaStackSlot *params, int index)
{
    return ushim_type_nested_dynarray_element_has_references(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT const char *ushim_callback_get_parameter_nested_element_type_name(UmkaStackSlot *params, int index)
{
    return ushim_type_nested_dynarray_element_name(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_map_key_kind(UmkaStackSlot *params, int index)
{
    return ushim_type_map_key_kind(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_map_key_size(UmkaStackSlot *params, int index)
{
    return ushim_type_map_key_size(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_map_key_has_references(UmkaStackSlot *params, int index)
{
    return ushim_type_map_key_has_references(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT const char *ushim_callback_get_parameter_map_key_type_name(UmkaStackSlot *params, int index)
{
    return ushim_type_map_key_name(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_map_value_kind(UmkaStackSlot *params, int index)
{
    return ushim_type_map_value_kind(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_map_value_size(UmkaStackSlot *params, int index)
{
    return ushim_type_map_value_size(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_map_value_has_references(UmkaStackSlot *params, int index)
{
    return ushim_type_map_value_has_references(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT const char *ushim_callback_get_parameter_map_value_type_name(UmkaStackSlot *params, int index)
{
    return ushim_type_map_value_name(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_map_value_element_kind(UmkaStackSlot *params, int index)
{
    return ushim_type_map_value_element_kind(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_map_value_element_size(UmkaStackSlot *params, int index)
{
    return ushim_type_map_value_element_size(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_map_value_element_has_references(UmkaStackSlot *params, int index)
{
    return ushim_type_map_value_element_has_references(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT const char *ushim_callback_get_parameter_map_value_element_type_name(UmkaStackSlot *params, int index)
{
    return ushim_type_map_value_element_name(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_is_variadic_parameter_list(UmkaStackSlot *params, int index)
{
    return ushim_type_is_variadic_parameter_list(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_has_references(UmkaStackSlot *params, int index)
{
    const Type *type = ushim_callback_get_parameter_type(params, index);
    return ushim_type_has_references(type);
}

USHIM_EXPORT const char *ushim_callback_get_parameter_type_name(UmkaStackSlot *params, int index)
{
    return ushim_type_name(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_is_enum(UmkaStackSlot *params, int index)
{
    return ushim_type_is_enum(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_enum_member_count(UmkaStackSlot *params, int index)
{
    return ushim_type_enum_member_count(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT const char *ushim_callback_get_parameter_enum_member_name(UmkaStackSlot *params, int parameterIndex, int memberIndex)
{
    return ushim_type_enum_member_name(ushim_callback_get_parameter_type(params, parameterIndex), memberIndex);
}

USHIM_EXPORT int64_t ushim_callback_get_parameter_enum_member_signed_value(UmkaStackSlot *params, int parameterIndex, int memberIndex)
{
    return ushim_type_enum_member_signed_value(ushim_callback_get_parameter_type(params, parameterIndex), memberIndex);
}

USHIM_EXPORT uint64_t ushim_callback_get_parameter_enum_member_unsigned_value(UmkaStackSlot *params, int parameterIndex, int memberIndex)
{
    return ushim_type_enum_member_unsigned_value(ushim_callback_get_parameter_type(params, parameterIndex), memberIndex);
}

USHIM_EXPORT int ushim_callback_get_result_kind(UmkaStackSlot *params, UmkaStackSlot *result)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    return type ? ushim_type_kind(type) : TYPE_VOID;
}

USHIM_EXPORT int ushim_callback_get_result_size(UmkaStackSlot *params, UmkaStackSlot *result)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    return ushim_type_size(type);
}

USHIM_EXPORT int ushim_callback_get_result_item_count(UmkaStackSlot *params, UmkaStackSlot *result)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    return ushim_type_item_count(type);
}

USHIM_EXPORT int ushim_callback_get_result_element_kind(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_element_kind(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_element_size(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_element_size(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_element_has_references(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_element_has_references(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT const char *ushim_callback_get_result_element_type_name(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_element_name(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_nested_element_kind(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_nested_dynarray_element_kind(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_nested_element_size(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_nested_dynarray_element_size(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_nested_element_has_references(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_nested_dynarray_element_has_references(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT const char *ushim_callback_get_result_nested_element_type_name(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_nested_dynarray_element_name(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_map_key_kind(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_map_key_kind(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_map_key_size(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_map_key_size(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_map_key_has_references(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_map_key_has_references(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT const char *ushim_callback_get_result_map_key_type_name(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_map_key_name(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_map_value_kind(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_map_value_kind(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_map_value_size(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_map_value_size(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_map_value_has_references(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_map_value_has_references(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT const char *ushim_callback_get_result_map_value_type_name(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_map_value_name(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_map_value_element_kind(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_map_value_element_kind(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_map_value_element_size(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_map_value_element_size(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_map_value_element_has_references(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_map_value_element_has_references(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT const char *ushim_callback_get_result_map_value_element_type_name(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_map_value_element_name(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_is_variadic_parameter_list(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_is_variadic_parameter_list(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_has_references(UmkaStackSlot *params, UmkaStackSlot *result)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    return ushim_type_has_references(type);
}

USHIM_EXPORT const char *ushim_callback_get_result_type_name(UmkaStackSlot *params, UmkaStackSlot *result)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    return type ? ushim_type_name(type) : "void";
}

USHIM_EXPORT int ushim_callback_get_result_is_enum(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_is_enum(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT int ushim_callback_get_result_enum_member_count(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_type_enum_member_count(ushim_callback_get_result_type(params, result));
}

USHIM_EXPORT const char *ushim_callback_get_result_enum_member_name(UmkaStackSlot *params, UmkaStackSlot *result, int memberIndex)
{
    return ushim_type_enum_member_name(ushim_callback_get_result_type(params, result), memberIndex);
}

USHIM_EXPORT int64_t ushim_callback_get_result_enum_member_signed_value(UmkaStackSlot *params, UmkaStackSlot *result, int memberIndex)
{
    return ushim_type_enum_member_signed_value(ushim_callback_get_result_type(params, result), memberIndex);
}

USHIM_EXPORT uint64_t ushim_callback_get_result_enum_member_unsigned_value(UmkaStackSlot *params, UmkaStackSlot *result, int memberIndex)
{
    return ushim_type_enum_member_unsigned_value(ushim_callback_get_result_type(params, result), memberIndex);
}

USHIM_EXPORT int64_t ushim_callback_get_param_int(UmkaStackSlot *params, int index)
{
    UmkaStackSlot *slot = ushim_param(params, index);
    return slot ? slot->intVal : 0;
}

USHIM_EXPORT uint64_t ushim_callback_get_param_uint(UmkaStackSlot *params, int index)
{
    UmkaStackSlot *slot = ushim_param(params, index);
    return slot ? slot->uintVal : 0;
}

USHIM_EXPORT double ushim_callback_get_param_real(UmkaStackSlot *params, int index)
{
    UmkaStackSlot *slot = ushim_param(params, index);
    const Type *type = ushim_callback_get_parameter_type(params, index);
    if (!slot)
        return 0.0;

    return ushim_type_kind(type) == TYPE_REAL32 ? slot->real32Val : slot->realVal;
}

USHIM_EXPORT void *ushim_callback_get_param_ptr(UmkaStackSlot *params, int index)
{
    UmkaStackSlot *slot = ushim_param(params, index);
    return slot ? slot->ptrVal : NULL;
}

USHIM_EXPORT const char *ushim_callback_get_param_string(UmkaStackSlot *params, int index)
{
    UmkaStackSlot *slot = ushim_param(params, index);
    return slot ? (const char *)slot->ptrVal : NULL;
}

USHIM_EXPORT int ushim_callback_retain_param(
    UmkaStackSlot *params,
    UmkaStackSlot *result,
    int index,
    UmkaHostHandle **handle)
{
    if (handle)
        *handle = NULL;

    Umka *umka = umkaGetInstance(result);
    const Type *type = ushim_callback_get_parameter_type(params, index);
    UmkaStackSlot *slot = ushim_param(params, index);
    if (!umka || !type || !slot || !handle)
        return 1;

    UmkaStackSlot value = ushim_value_from_storage(type, slot);
    return ushim_native_value_retain(umka, type, value, handle);
}

USHIM_EXPORT int ushim_callback_get_param_data(UmkaStackSlot *params, int index, void *buffer, int size)
{
    UmkaStackSlot *slot = ushim_param(params, index);
    const Type *type = ushim_callback_get_parameter_type(params, index);
    if (!slot || !type || !buffer || size != ushim_type_size(type) || ushim_type_has_references(type))
        return 1;

    memcpy(buffer, slot, size);
    return 0;
}

static DynArray *ushim_callback_param_dynarray(UmkaStackSlot *params, int index)
{
    const Type *type = ushim_callback_get_parameter_type(params, index);
    UmkaStackSlot *slot = ushim_param(params, index);
    if (ushim_type_kind(type) != TYPE_DYNARRAY || !slot)
        return NULL;

    return (DynArray *)slot;
}

USHIM_EXPORT int ushim_callback_get_param_dynarray_length(UmkaStackSlot *params, int index)
{
    DynArray *array = ushim_callback_param_dynarray(params, index);
    return array ? umkaGetDynArrayLen(array) : -1;
}

USHIM_EXPORT int ushim_callback_copy_param_dynarray_data(UmkaStackSlot *params, int index, void *buffer, int size)
{
    DynArray *array = ushim_callback_param_dynarray(params, index);
    const Type *type = ushim_callback_get_parameter_type(params, index);
    int byteCount = 0;
    if (!array
        || !type
        || ushim_type_kind(type) != TYPE_DYNARRAY
        || ushim_type_element_has_references(type)
        || ushim_dynarray_byte_count(array, &byteCount) != 0
        || size != byteCount
        || (!buffer && byteCount > 0))
    {
        return 1;
    }

    if (byteCount > 0)
        memcpy(buffer, array->data, (size_t)byteCount);

    return 0;
}

USHIM_EXPORT int ushim_callback_get_param_dynarray_string(UmkaStackSlot *params, int index, int elementIndex, const char **value)
{
    DynArray *array = ushim_callback_param_dynarray(params, index);
    const Type *type = ushim_callback_get_parameter_type(params, index);
    const int len = array ? umkaGetDynArrayLen(array) : -1;
    if (!value
        || !array
        || !ushim_dynarray_type_accepts_strings(type)
        || elementIndex < 0
        || elementIndex >= len)
    {
        return 1;
    }

    const char **items = (const char **)array->data;
    *value = items[elementIndex];
    return 0;
}

USHIM_EXPORT int ushim_callback_get_param_nested_dynarray_length(UmkaStackSlot *params, int index, int elementIndex)
{
    DynArray *outer = ushim_callback_param_dynarray(params, index);
    const Type *type = ushim_callback_get_parameter_type(params, index);
    if (!outer || !ushim_dynarray_type_accepts_nested_bytes(type, ushim_type_nested_dynarray_element_size(type)))
        return -1;

    DynArray *inner = ushim_dynarray_nested_item(outer, elementIndex);
    return inner ? umkaGetDynArrayLen(inner) : -1;
}

USHIM_EXPORT int ushim_callback_copy_param_nested_dynarray_data(
    UmkaStackSlot *params,
    int index,
    int elementIndex,
    void *buffer,
    int size,
    int elementSize)
{
    DynArray *outer = ushim_callback_param_dynarray(params, index);
    const Type *type = ushim_callback_get_parameter_type(params, index);
    DynArray *inner = ushim_dynarray_nested_item(outer, elementIndex);
    int byteCount = 0;
    if (!outer
        || !inner
        || !ushim_dynarray_type_accepts_nested_bytes(type, elementSize)
        || inner->itemSize != elementSize
        || ushim_dynarray_byte_count(inner, &byteCount) != 0
        || size != byteCount
        || (!buffer && byteCount > 0))
    {
        return 1;
    }

    if (byteCount > 0)
        memcpy(buffer, inner->data, (size_t)byteCount);

    return 0;
}

USHIM_EXPORT int ushim_callback_get_param_nested_string_array_length(UmkaStackSlot *params, int index, int elementIndex)
{
    DynArray *outer = ushim_callback_param_dynarray(params, index);
    const Type *type = ushim_callback_get_parameter_type(params, index);
    if (!outer || !ushim_dynarray_type_accepts_nested_strings(type))
        return -1;

    DynArray *inner = ushim_dynarray_nested_item(outer, elementIndex);
    return inner ? umkaGetDynArrayLen(inner) : -1;
}

USHIM_EXPORT int ushim_callback_copy_param_nested_string_array_data(
    UmkaStackSlot *params,
    int index,
    int elementIndex,
    const char **values,
    int valueCount)
{
    DynArray *outer = ushim_callback_param_dynarray(params, index);
    const Type *type = ushim_callback_get_parameter_type(params, index);
    DynArray *inner = ushim_dynarray_nested_item(outer, elementIndex);
    const int len = inner ? umkaGetDynArrayLen(inner) : -1;
    if (!outer
        || !inner
        || !ushim_dynarray_type_accepts_nested_strings(type)
        || inner->itemSize != (int)sizeof(char *)
        || len < 0
        || valueCount != len
        || (!values && len > 0))
    {
        return 1;
    }

    if (len > 0)
        memcpy(values, inner->data, (size_t)len * sizeof(char *));

    return 0;
}

static Map *ushim_callback_param_map(UmkaStackSlot *params, int index)
{
    const Type *type = ushim_callback_get_parameter_type(params, index);
    UmkaStackSlot *slot = ushim_param(params, index);
    if (ushim_type_kind(type) != TYPE_MAP || !slot)
        return NULL;

    return (Map *)slot;
}

USHIM_EXPORT int ushim_callback_get_param_map_count(UmkaStackSlot *params, int index)
{
    return ushim_map_count(ushim_callback_param_map(params, index));
}

USHIM_EXPORT int ushim_callback_copy_param_map_entries(
    UmkaStackSlot *params,
    int index,
    void *keys,
    int keyBytes,
    void *values,
    int valueBytes)
{
    return ushim_copy_map_entries(
        ushim_callback_param_map(params, index),
        ushim_callback_get_parameter_type(params, index),
        keys,
        keyBytes,
        values,
        valueBytes);
}

USHIM_EXPORT int ushim_callback_copy_param_string_key_map_entries(
    UmkaStackSlot *params,
    int index,
    const char **keys,
    int keyCount,
    void *values,
    int valueBytes)
{
    return ushim_copy_map_string_key_entries(
        ushim_callback_param_map(params, index),
        ushim_callback_get_parameter_type(params, index),
        keys,
        keyCount,
        values,
        valueBytes);
}

USHIM_EXPORT int ushim_callback_copy_param_string_value_map_entries(
    UmkaStackSlot *params,
    int index,
    void *keys,
    int keyBytes,
    const char **values,
    int valueCount)
{
    return ushim_copy_map_string_value_entries(
        ushim_callback_param_map(params, index),
        ushim_callback_get_parameter_type(params, index),
        keys,
        keyBytes,
        values,
        valueCount);
}

USHIM_EXPORT int ushim_callback_copy_param_string_map_entries(
    UmkaStackSlot *params,
    int index,
    const char **keys,
    int keyCount,
    const char **values,
    int valueCount)
{
    return ushim_copy_map_string_entries(
        ushim_callback_param_map(params, index),
        ushim_callback_get_parameter_type(params, index),
        keys,
        keyCount,
        values,
        valueCount);
}

USHIM_EXPORT int ushim_callback_copy_param_map_dynarray_value_entries(
    UmkaStackSlot *params,
    int index,
    void *keys,
    int keyBytes,
    int *lengths,
    int lengthCount,
    int elementSize)
{
    return ushim_copy_map_dynarray_value_entries(
        ushim_callback_param_map(params, index),
        ushim_callback_get_parameter_type(params, index),
        keys,
        keyBytes,
        lengths,
        lengthCount,
        elementSize);
}

USHIM_EXPORT int ushim_callback_copy_param_map_dynarray_value_data(
    UmkaStackSlot *params,
    int index,
    int entryIndex,
    void *buffer,
    int size,
    int elementSize)
{
    return ushim_copy_map_dynarray_value_data(
        ushim_callback_param_map(params, index),
        ushim_callback_get_parameter_type(params, index),
        entryIndex,
        buffer,
        size,
        elementSize);
}

USHIM_EXPORT int ushim_callback_copy_param_map_string_array_value_entries(
    UmkaStackSlot *params,
    int index,
    void *keys,
    int keyBytes,
    int *lengths,
    int lengthCount)
{
    return ushim_copy_map_string_array_value_entries(
        ushim_callback_param_map(params, index),
        ushim_callback_get_parameter_type(params, index),
        keys,
        keyBytes,
        lengths,
        lengthCount);
}

USHIM_EXPORT int ushim_callback_copy_param_map_string_array_value_data(
    UmkaStackSlot *params,
    int index,
    int entryIndex,
    const char **values,
    int valueCount)
{
    return ushim_copy_map_string_array_value_data(
        ushim_callback_param_map(params, index),
        ushim_callback_get_parameter_type(params, index),
        entryIndex,
        values,
        valueCount);
}

USHIM_EXPORT int ushim_callback_copy_param_string_key_map_dynarray_value_entries(
    UmkaStackSlot *params,
    int index,
    const char **keys,
    int keyCount,
    int *lengths,
    int lengthCount,
    int elementSize)
{
    return ushim_copy_map_string_key_dynarray_value_entries(
        ushim_callback_param_map(params, index),
        ushim_callback_get_parameter_type(params, index),
        keys,
        keyCount,
        lengths,
        lengthCount,
        elementSize);
}

USHIM_EXPORT int ushim_callback_copy_param_string_key_map_dynarray_value_data(
    UmkaStackSlot *params,
    int index,
    int entryIndex,
    void *buffer,
    int size,
    int elementSize)
{
    return ushim_copy_map_string_key_dynarray_value_data(
        ushim_callback_param_map(params, index),
        ushim_callback_get_parameter_type(params, index),
        entryIndex,
        buffer,
        size,
        elementSize);
}

USHIM_EXPORT int ushim_callback_copy_param_string_key_map_string_array_value_entries(
    UmkaStackSlot *params,
    int index,
    const char **keys,
    int keyCount,
    int *lengths,
    int lengthCount)
{
    return ushim_copy_map_string_key_string_array_value_entries(
        ushim_callback_param_map(params, index),
        ushim_callback_get_parameter_type(params, index),
        keys,
        keyCount,
        lengths,
        lengthCount);
}

USHIM_EXPORT int ushim_callback_copy_param_string_key_map_string_array_value_data(
    UmkaStackSlot *params,
    int index,
    int entryIndex,
    const char **values,
    int valueCount)
{
    return ushim_copy_map_string_key_string_array_value_data(
        ushim_callback_param_map(params, index),
        ushim_callback_get_parameter_type(params, index),
        entryIndex,
        values,
        valueCount);
}

USHIM_EXPORT void ushim_callback_set_result_int(UmkaStackSlot *params, UmkaStackSlot *result, int64_t value)
{
    umkaGetResult(params, result)->intVal = value;
}

USHIM_EXPORT void ushim_callback_set_result_uint(UmkaStackSlot *params, UmkaStackSlot *result, uint64_t value)
{
    umkaGetResult(params, result)->uintVal = value;
}

USHIM_EXPORT void ushim_callback_set_result_real(UmkaStackSlot *params, UmkaStackSlot *result, double value)
{
    umkaGetResult(params, result)->realVal = value;
}

USHIM_EXPORT void ushim_callback_set_result_ptr(UmkaStackSlot *params, UmkaStackSlot *result, void *value)
{
    umkaGetResult(params, result)->ptrVal = value;
}

USHIM_EXPORT void ushim_callback_set_result_string(UmkaStackSlot *params, UmkaStackSlot *result, const char *value)
{
    Umka *umka = umkaGetInstance(result);
    umkaGetResult(params, result)->ptrVal = value ? umkaMakeStr(umka, value) : NULL;
}

static int ushim_callback_set_result_any(
    UmkaStackSlot *params,
    UmkaStackSlot *result,
    int payloadKind,
    UmkaStackSlot payload,
    const char *stringValue,
    UmkaHostHandle *handle)
{
    Umka *umka = umkaGetInstance(result);
    const Type *targetType = ushim_callback_get_result_type(params, result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    void *dest = ushim_callback_result_assignment_target(targetType, slot);
    const Type *payloadType = NULL;

    if (!umka || !dest || !targetType)
        return 1;

    if (handle)
    {
        if (!umkaHostHandleValid(handle))
            return 1;

        payloadType = (const Type *)umkaGetHostHandleType(handle);
        payload = umkaGetHostHandleValue(handle);
    }
    else
    {
        payloadType = ushim_predecl_any_payload_type(umka, payloadKind);
        if (payloadKind != TYPE_NONE && !payloadType)
            return 1;

        if (payloadKind == TYPE_STR)
            payload.ptrVal = stringValue ? umkaMakeStr(umka, stringValue) : NULL;
    }

    const int status = ushim_any_assign_to_storage(umka, targetType, dest, payloadType, payload);

    if (!handle && payloadKind == TYPE_STR && payload.ptrVal)
        umkaDecRef(umka, payload.ptrVal);

    return status;
}

USHIM_EXPORT int ushim_callback_set_result_any_null(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_callback_set_result_any(params, result, TYPE_NONE, (UmkaStackSlot){0}, NULL, NULL);
}

USHIM_EXPORT int ushim_callback_set_result_any_int(UmkaStackSlot *params, UmkaStackSlot *result, int64_t value)
{
    UmkaStackSlot payload = {0};
    payload.intVal = value;
    return ushim_callback_set_result_any(params, result, TYPE_INT, payload, NULL, NULL);
}

USHIM_EXPORT int ushim_callback_set_result_any_uint(UmkaStackSlot *params, UmkaStackSlot *result, uint64_t value)
{
    UmkaStackSlot payload = {0};
    payload.uintVal = value;
    return ushim_callback_set_result_any(params, result, TYPE_UINT, payload, NULL, NULL);
}

USHIM_EXPORT int ushim_callback_set_result_any_char(UmkaStackSlot *params, UmkaStackSlot *result, uint64_t value)
{
    UmkaStackSlot payload = {0};
    payload.uintVal = value;
    return ushim_callback_set_result_any(params, result, TYPE_CHAR, payload, NULL, NULL);
}

USHIM_EXPORT int ushim_callback_set_result_any_real(UmkaStackSlot *params, UmkaStackSlot *result, double value)
{
    UmkaStackSlot payload = {0};
    payload.realVal = value;
    return ushim_callback_set_result_any(params, result, TYPE_REAL, payload, NULL, NULL);
}

USHIM_EXPORT int ushim_callback_set_result_any_bool(UmkaStackSlot *params, UmkaStackSlot *result, int value)
{
    UmkaStackSlot payload = {0};
    payload.intVal = value ? 1 : 0;
    return ushim_callback_set_result_any(params, result, TYPE_BOOL, payload, NULL, NULL);
}

USHIM_EXPORT int ushim_callback_set_result_any_string(UmkaStackSlot *params, UmkaStackSlot *result, const char *value)
{
    return ushim_callback_set_result_any(params, result, TYPE_STR, (UmkaStackSlot){0}, value, NULL);
}

USHIM_EXPORT int ushim_callback_set_result_any_native_value(UmkaStackSlot *params, UmkaStackSlot *result, UmkaHostHandle *handle)
{
    return ushim_callback_set_result_any(params, result, TYPE_NONE, (UmkaStackSlot){0}, NULL, handle);
}

USHIM_EXPORT int ushim_callback_set_result_data(UmkaStackSlot *params, UmkaStackSlot *result, const void *value, int size)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    if (!type || !slot || !slot->ptrVal || !value || size != ushim_type_size(type) || ushim_type_has_references(type))
        return 1;

    memcpy(slot->ptrVal, value, size);
    return 0;
}

USHIM_EXPORT int ushim_callback_set_result_native_value(
    UmkaStackSlot *params,
    UmkaStackSlot *result,
    UmkaHostHandle *handle)
{
    Umka *umka = umkaGetInstance(result);
    const Type *type = ushim_callback_get_result_type(params, result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    void *dest = ushim_callback_result_assignment_target(type, slot);
    if (!umka || !type || !dest || !umkaHostHandleValid(handle))
        return 1;

    UmkaStackSlot value = umkaGetHostHandleValue(handle);
    if (ushim_native_value_type_matches(type, handle))
        return ushim_native_value_assign_to_storage(type, dest, value);

    return ushim_native_value_assign_to_interface_storage(umka, type, dest, handle);
}

USHIM_EXPORT int ushim_callback_set_result_dynarray(
    UmkaStackSlot *params,
    UmkaStackSlot *result,
    const void *value,
    int length,
    int elementSize)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    Umka *umka = umkaGetInstance(result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    if (!slot
        || !slot->ptrVal
        || !umka
        || !ushim_dynarray_type_accepts_bytes(type, elementSize)
        || length < 0
        || (!value && length > 0))
    {
        return 1;
    }

    DynArray *array = (DynArray *)slot->ptrVal;
    memset(array, 0, sizeof(DynArray));
    umkaMakeDynArray(umka, array, (const UmkaType *)type, length);

    const int64_t bytes = (int64_t)length * elementSize;
    if (bytes > INT_MAX)
        return 1;

    if (bytes > 0)
        memcpy(array->data, value, (size_t)bytes);

    return 0;
}

USHIM_EXPORT int ushim_callback_set_result_dynarray_strings(
    UmkaStackSlot *params,
    UmkaStackSlot *result,
    const char **values,
    int length)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    Umka *umka = umkaGetInstance(result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    if (!slot
        || !slot->ptrVal
        || !umka
        || !ushim_dynarray_type_accepts_strings(type)
        || length < 0
        || (!values && length > 0))
    {
        return 1;
    }

    DynArray *array = (DynArray *)slot->ptrVal;
    memset(array, 0, sizeof(DynArray));
    umkaMakeDynArray(umka, array, (const UmkaType *)type, length);

    char **items = (char **)array->data;
    for (int i = 0; i < length; i++)
        items[i] = values[i] ? umkaMakeStr(umka, values[i]) : NULL;

    return 0;
}

USHIM_EXPORT int ushim_callback_set_result_nested_dynarray(
    UmkaStackSlot *params,
    UmkaStackSlot *result,
    const int *lengths,
    int lengthCount,
    const void *values,
    int valueByteCount,
    int elementSize)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    Umka *umka = umkaGetInstance(result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    if (!slot || !slot->ptrVal)
        return 1;

    DynArray *array = (DynArray *)slot->ptrVal;
    memset(array, 0, sizeof(DynArray));
    return ushim_set_nested_dynarray_bytes(
        umka,
        array,
        type,
        lengths,
        lengthCount,
        values,
        valueByteCount,
        elementSize);
}

USHIM_EXPORT int ushim_callback_set_result_nested_dynarray_strings(
    UmkaStackSlot *params,
    UmkaStackSlot *result,
    const int *lengths,
    int lengthCount,
    const char **values,
    int valueCount)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    Umka *umka = umkaGetInstance(result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    if (!slot || !slot->ptrVal)
        return 1;

    DynArray *array = (DynArray *)slot->ptrVal;
    memset(array, 0, sizeof(DynArray));
    return ushim_set_nested_dynarray_strings(
        umka,
        array,
        type,
        lengths,
        lengthCount,
        values,
        valueCount);
}

USHIM_EXPORT int ushim_callback_set_result_map(
    UmkaStackSlot *params,
    UmkaStackSlot *result,
    const void *keys,
    int keyBytes,
    const void *values,
    int valueBytes)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    Umka *umka = umkaGetInstance(result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    if (!slot || !slot->ptrVal)
        return 1;

    return ushim_set_map_bytes(
        umka,
        (Map *)slot->ptrVal,
        type,
        keys,
        keyBytes,
        values,
        valueBytes);
}

USHIM_EXPORT int ushim_callback_set_result_string_key_map(
    UmkaStackSlot *params,
    UmkaStackSlot *result,
    const char **keys,
    int keyCount,
    const void *values,
    int valueBytes)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    Umka *umka = umkaGetInstance(result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    if (!slot || !slot->ptrVal)
        return 1;

    return ushim_set_map_string_key_bytes_value(
        umka,
        (Map *)slot->ptrVal,
        type,
        keys,
        keyCount,
        values,
        valueBytes);
}

USHIM_EXPORT int ushim_callback_set_result_string_value_map(
    UmkaStackSlot *params,
    UmkaStackSlot *result,
    const void *keys,
    int keyBytes,
    const char **values,
    int valueCount)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    Umka *umka = umkaGetInstance(result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    if (!slot || !slot->ptrVal)
        return 1;

    return ushim_set_map_bytes_key_string_value(
        umka,
        (Map *)slot->ptrVal,
        type,
        keys,
        keyBytes,
        values,
        valueCount);
}

USHIM_EXPORT int ushim_callback_set_result_string_map(
    UmkaStackSlot *params,
    UmkaStackSlot *result,
    const char **keys,
    int keyCount,
    const char **values,
    int valueCount)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    Umka *umka = umkaGetInstance(result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    if (!slot || !slot->ptrVal)
        return 1;

    return ushim_set_map_string_key_string_value(
        umka,
        (Map *)slot->ptrVal,
        type,
        keys,
        keyCount,
        values,
        valueCount);
}

USHIM_EXPORT const char *ushim_error_file_name(Umka *umka)
{
    return umka ? umkaGetError(umka)->fileName : NULL;
}

USHIM_EXPORT const char *ushim_error_function_name(Umka *umka)
{
    return umka ? umkaGetError(umka)->fnName : NULL;
}

USHIM_EXPORT const char *ushim_error_message(Umka *umka)
{
    return umka ? umkaGetError(umka)->msg : NULL;
}

USHIM_EXPORT int ushim_error_line(Umka *umka)
{
    return umka ? umkaGetError(umka)->line : 0;
}

USHIM_EXPORT int ushim_error_position(Umka *umka)
{
    return umka ? umkaGetError(umka)->pos : 0;
}

USHIM_EXPORT int ushim_error_code(Umka *umka)
{
    return umka ? umkaGetError(umka)->code : 0;
}
