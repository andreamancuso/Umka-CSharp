#define __USE_MINGW_ANSI_STDIO 1

#include <stdarg.h>
#include <stdlib.h>
#include <string.h>

#include "umka_compiler.h"
#include "umka_api.h"

#ifdef _WIN32
#define USHIM_EXPORT __declspec(dllexport)
#else
#define USHIM_EXPORT __attribute__((visibility("default")))
#endif

typedef int (*UshimManagedCallback)(void *state, UmkaStackSlot *params, UmkaStackSlot *result);

typedef struct
{
    UshimManagedCallback callback;
    void *state;
} UshimCallbackSlot;

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
    int fileSystemEnabled,
    int implLibsEnabled,
    Umka **runtime)
{
    if (!runtime)
        return 1;

    *runtime = umkaAlloc();
    if (!*runtime)
        return 1;

    if (!umkaInit(*runtime, fileName, source, stackSize, NULL, 0, NULL, fileSystemEnabled != 0, implLibsEnabled != 0, NULL))
        return ushim_status(*runtime);

    return 0;
}

USHIM_EXPORT void ushim_free(Umka *umka)
{
    if (umka)
        umkaFree(umka);
}

USHIM_EXPORT int ushim_compile(Umka *umka)
{
    if (!umkaCompile(umka))
        return ushim_status(umka);
    return 0;
}

USHIM_EXPORT int ushim_run(Umka *umka)
{
    return umkaRun(umka);
}

USHIM_EXPORT int ushim_alive(Umka *umka)
{
    return umka && umkaAlive(umka);
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
    return umkaGetFunc(umka, fn->moduleName, fn->functionName, fn->function) ? 0 : 1;
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

static UmkaStackSlot *ushim_param(UmkaStackSlot *params, int index)
{
    return params ? umkaGetParam(params, index) : NULL;
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
    if (!function || !function->params || !function->result)
        return 0;

    const UmkaType *type = umkaGetResultType(function->params, function->result);
    return type ? ((const Type *)type)->size : 0;
}

USHIM_EXPORT int ushim_context_set_result_buffer(UmkaFuncContext *function, void *buffer)
{
    if (!function || !function->result)
        return 1;

    function->result->ptrVal = buffer;
    return 0;
}

USHIM_EXPORT const char *ushim_context_get_result_string(UmkaFuncContext *function)
{
    UmkaStackSlot *slot = ushim_result(function);
    return slot ? (const char *)slot->ptrVal : NULL;
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
    return slot ? slot->realVal : 0.0;
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
