#define __USE_MINGW_ANSI_STDIO 1

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
    if (!fnIdent || !fnIdent->isExported || fnIdent->kind != IDENT_CONST || fnIdent->type->kind != TYPE_FN)
        return USHIM_NOT_FOUND;

    identSetUsed(fnIdent);
    compilerMakeFuncContext(umka, fnIdent->type, fnIdent->offset, fn->function);
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

static UmkaStackSlot *ushim_param(UmkaStackSlot *params, int index)
{
    return params ? umkaGetParam(params, index) : NULL;
}

static const Type *ushim_context_get_parameter_type(UmkaFuncContext *function, int index);
static const Type *ushim_context_get_result_type(UmkaFuncContext *function);
static const Type *ushim_callback_get_parameter_type(UmkaStackSlot *params, int index);
static const Type *ushim_callback_get_result_type(UmkaStackSlot *params, UmkaStackSlot *result);

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
    if (type && type->kind == TYPE_REAL32)
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
    return type ? ((const Type *)type)->size : 0;
}

USHIM_EXPORT int ushim_context_get_result_item_count(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_result_type(function);
    return type ? type->numItems : 0;
}

USHIM_EXPORT int ushim_context_get_argument_count(UmkaFuncContext *function)
{
    if (!function || !function->params)
        return 0;

    int count = 0;
    while (umkaGetParamType(function->params, count))
        count++;
    return count;
}

static const Type *ushim_context_get_parameter_type(UmkaFuncContext *function, int index)
{
    if (!function || !function->params)
        return NULL;

    return (const Type *)umkaGetParamType(function->params, index);
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
    return typeSpelling(type, buf);
}

USHIM_EXPORT int ushim_context_get_parameter_kind(UmkaFuncContext *function, int index)
{
    const Type *type = ushim_context_get_parameter_type(function, index);
    return type ? type->kind : TYPE_NONE;
}

USHIM_EXPORT int ushim_context_get_parameter_size(UmkaFuncContext *function, int index)
{
    const Type *type = ushim_context_get_parameter_type(function, index);
    return type ? type->size : 0;
}

USHIM_EXPORT int ushim_context_get_parameter_item_count(UmkaFuncContext *function, int index)
{
    const Type *type = ushim_context_get_parameter_type(function, index);
    return type ? type->numItems : 0;
}

USHIM_EXPORT int ushim_context_get_parameter_has_references(UmkaFuncContext *function, int index)
{
    const Type *type = ushim_context_get_parameter_type(function, index);
    return type && type->isGarbageCollected ? 1 : 0;
}

USHIM_EXPORT int ushim_context_get_result_has_references(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_result_type(function);
    return type && type->isGarbageCollected ? 1 : 0;
}

USHIM_EXPORT const char *ushim_context_get_parameter_type_name(UmkaFuncContext *function, int index)
{
    return ushim_type_name(ushim_context_get_parameter_type(function, index));
}

USHIM_EXPORT int ushim_context_get_result_kind(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_result_type(function);
    return type ? type->kind : TYPE_VOID;
}

USHIM_EXPORT const char *ushim_context_get_result_type_name(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_result_type(function);
    return type ? ushim_type_name(type) : "void";
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
    if (!slot || !type || !value || size != type->size || type->isGarbageCollected)
        return 1;

    memcpy(slot, value, size);
    return 0;
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
    return type ? type->kind : TYPE_NONE;
}

USHIM_EXPORT int ushim_callback_get_parameter_size(UmkaStackSlot *params, int index)
{
    const Type *type = ushim_callback_get_parameter_type(params, index);
    return type ? type->size : 0;
}

USHIM_EXPORT int ushim_callback_get_parameter_item_count(UmkaStackSlot *params, int index)
{
    const Type *type = ushim_callback_get_parameter_type(params, index);
    return type ? type->numItems : 0;
}

USHIM_EXPORT int ushim_callback_get_parameter_has_references(UmkaStackSlot *params, int index)
{
    const Type *type = ushim_callback_get_parameter_type(params, index);
    return type && type->isGarbageCollected ? 1 : 0;
}

USHIM_EXPORT const char *ushim_callback_get_parameter_type_name(UmkaStackSlot *params, int index)
{
    return ushim_type_name(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_result_kind(UmkaStackSlot *params, UmkaStackSlot *result)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    return type ? type->kind : TYPE_VOID;
}

USHIM_EXPORT int ushim_callback_get_result_size(UmkaStackSlot *params, UmkaStackSlot *result)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    return type ? type->size : 0;
}

USHIM_EXPORT int ushim_callback_get_result_item_count(UmkaStackSlot *params, UmkaStackSlot *result)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    return type ? type->numItems : 0;
}

USHIM_EXPORT int ushim_callback_get_result_has_references(UmkaStackSlot *params, UmkaStackSlot *result)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    return type && type->isGarbageCollected ? 1 : 0;
}

USHIM_EXPORT const char *ushim_callback_get_result_type_name(UmkaStackSlot *params, UmkaStackSlot *result)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    return type ? ushim_type_name(type) : "void";
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

    return type && type->kind == TYPE_REAL32 ? slot->real32Val : slot->realVal;
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

USHIM_EXPORT int ushim_callback_get_param_data(UmkaStackSlot *params, int index, void *buffer, int size)
{
    UmkaStackSlot *slot = ushim_param(params, index);
    const Type *type = ushim_callback_get_parameter_type(params, index);
    if (!slot || !type || !buffer || size != type->size || type->isGarbageCollected)
        return 1;

    memcpy(buffer, slot, size);
    return 0;
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

USHIM_EXPORT int ushim_callback_set_result_data(UmkaStackSlot *params, UmkaStackSlot *result, const void *value, int size)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    if (!type || !slot || !slot->ptrVal || !value || size != type->size || type->isGarbageCollected)
        return 1;

    memcpy(slot->ptrVal, value, size);
    return 0;
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
