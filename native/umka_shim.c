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

static const Type *ushim_context_get_parameter_type(UmkaFuncContext *function, int index);
static const Type *ushim_context_get_result_type(UmkaFuncContext *function);
static const Type *ushim_callback_get_parameter_type(UmkaStackSlot *params, int index);
static const Type *ushim_callback_get_result_type(UmkaStackSlot *params, UmkaStackSlot *result);
static const Type *ushim_type_element_type(const Type *type);

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
    return ushim_context_get_explicit_argument_count(function);
}

USHIM_EXPORT int ushim_context_get_default_argument_count(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_function_type(function);
    return type && type->kind == TYPE_FN ? type->sig->numDefaultParams : 0;
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
    if (!fnType || fnType->kind != TYPE_FN)
        return 1;

    const int explicitCount = ushim_context_get_explicit_argument_count(function);
    if (index < 0 || index >= explicitCount)
        return 1;

    const int sigIndex = index + 1;    // Skip the hidden upvalue/self slot.
    const Param *param = fnType->sig->param[sigIndex];
    const Type *type = param ? param->type : NULL;
    UmkaStackSlot *slot = ushim_param(function->params, index);
    if (!type || !slot)
        return 1;

    const Const value = param->defaultVal;
    switch (type->kind)
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
    return typeSpelling(type, buf);
}

static const Type *ushim_enum_type(const Type *type)
{
    return type && type->isEnum ? type : NULL;
}

static const EnumConst *ushim_enum_member(const Type *type, int index)
{
    const Type *enumType = ushim_enum_type(type);
    if (!enumType || index < 0 || index >= enumType->numItems)
        return NULL;

    return enumType->enumConst[index];
}

static const Type *ushim_type_element_type(const Type *type)
{
    if (!type)
        return NULL;

    if (type->kind == TYPE_ARRAY || type->kind == TYPE_DYNARRAY)
        return type->base;

    return NULL;
}

static int ushim_type_element_kind(const Type *type)
{
    const Type *elementType = ushim_type_element_type(type);
    return elementType ? elementType->kind : TYPE_NONE;
}

static int ushim_type_element_size(const Type *type)
{
    const Type *elementType = ushim_type_element_type(type);
    return elementType ? elementType->size : 0;
}

static int ushim_type_element_has_references(const Type *type)
{
    const Type *elementType = ushim_type_element_type(type);
    return elementType && elementType->isGarbageCollected ? 1 : 0;
}

static const char *ushim_type_element_name(const Type *type)
{
    return ushim_type_name(ushim_type_element_type(type));
}

static const Type *ushim_type_nested_dynarray_element_type(const Type *type)
{
    const Type *elementType = ushim_type_element_type(type);
    if (!type || type->kind != TYPE_DYNARRAY || !elementType || elementType->kind != TYPE_DYNARRAY)
        return NULL;

    return ushim_type_element_type(elementType);
}

static int ushim_type_nested_dynarray_element_kind(const Type *type)
{
    const Type *elementType = ushim_type_nested_dynarray_element_type(type);
    return elementType ? elementType->kind : TYPE_NONE;
}

static int ushim_type_nested_dynarray_element_size(const Type *type)
{
    const Type *elementType = ushim_type_nested_dynarray_element_type(type);
    return elementType ? elementType->size : 0;
}

static int ushim_type_nested_dynarray_element_has_references(const Type *type)
{
    const Type *elementType = ushim_type_nested_dynarray_element_type(type);
    return elementType && elementType->isGarbageCollected ? 1 : 0;
}

static const char *ushim_type_nested_dynarray_element_name(const Type *type)
{
    return ushim_type_name(ushim_type_nested_dynarray_element_type(type));
}

static const Type *ushim_type_map_key_type(const Type *type)
{
    return type && type->kind == TYPE_MAP ? typeMapKey(type) : NULL;
}

static const Type *ushim_type_map_value_type(const Type *type)
{
    return type && type->kind == TYPE_MAP ? typeMapItem(type) : NULL;
}

static int ushim_type_map_key_kind(const Type *type)
{
    const Type *keyType = ushim_type_map_key_type(type);
    return keyType ? keyType->kind : TYPE_NONE;
}

static int ushim_type_map_value_kind(const Type *type)
{
    const Type *valueType = ushim_type_map_value_type(type);
    return valueType ? valueType->kind : TYPE_NONE;
}

static int ushim_type_map_key_size(const Type *type)
{
    const Type *keyType = ushim_type_map_key_type(type);
    return keyType ? keyType->size : 0;
}

static int ushim_type_map_value_size(const Type *type)
{
    const Type *valueType = ushim_type_map_value_type(type);
    return valueType ? valueType->size : 0;
}

static int ushim_type_map_key_has_references(const Type *type)
{
    const Type *keyType = ushim_type_map_key_type(type);
    return keyType && keyType->isGarbageCollected ? 1 : 0;
}

static int ushim_type_map_value_has_references(const Type *type)
{
    const Type *valueType = ushim_type_map_value_type(type);
    return valueType && valueType->isGarbageCollected ? 1 : 0;
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
    return type && type->isVariadicParamList ? 1 : 0;
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

USHIM_EXPORT int ushim_context_get_parameter_is_enum(UmkaFuncContext *function, int index)
{
    return ushim_enum_type(ushim_context_get_parameter_type(function, index)) ? 1 : 0;
}

USHIM_EXPORT int ushim_context_get_parameter_enum_member_count(UmkaFuncContext *function, int index)
{
    const Type *type = ushim_enum_type(ushim_context_get_parameter_type(function, index));
    return type ? type->numItems : 0;
}

USHIM_EXPORT const char *ushim_context_get_parameter_enum_member_name(UmkaFuncContext *function, int parameterIndex, int memberIndex)
{
    const EnumConst *member = ushim_enum_member(ushim_context_get_parameter_type(function, parameterIndex), memberIndex);
    return member ? member->name : NULL;
}

USHIM_EXPORT int64_t ushim_context_get_parameter_enum_member_signed_value(UmkaFuncContext *function, int parameterIndex, int memberIndex)
{
    const EnumConst *member = ushim_enum_member(ushim_context_get_parameter_type(function, parameterIndex), memberIndex);
    return member ? member->val.intVal : 0;
}

USHIM_EXPORT uint64_t ushim_context_get_parameter_enum_member_unsigned_value(UmkaFuncContext *function, int parameterIndex, int memberIndex)
{
    const EnumConst *member = ushim_enum_member(ushim_context_get_parameter_type(function, parameterIndex), memberIndex);
    return member ? member->val.uintVal : 0;
}

USHIM_EXPORT int ushim_context_get_result_kind(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_result_type(function);
    return type ? type->kind : TYPE_VOID;
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
    return ushim_enum_type(ushim_context_get_result_type(function)) ? 1 : 0;
}

USHIM_EXPORT int ushim_context_get_result_enum_member_count(UmkaFuncContext *function)
{
    const Type *type = ushim_enum_type(ushim_context_get_result_type(function));
    return type ? type->numItems : 0;
}

USHIM_EXPORT const char *ushim_context_get_result_enum_member_name(UmkaFuncContext *function, int memberIndex)
{
    const EnumConst *member = ushim_enum_member(ushim_context_get_result_type(function), memberIndex);
    return member ? member->name : NULL;
}

USHIM_EXPORT int64_t ushim_context_get_result_enum_member_signed_value(UmkaFuncContext *function, int memberIndex)
{
    const EnumConst *member = ushim_enum_member(ushim_context_get_result_type(function), memberIndex);
    return member ? member->val.intVal : 0;
}

USHIM_EXPORT uint64_t ushim_context_get_result_enum_member_unsigned_value(UmkaFuncContext *function, int memberIndex)
{
    const EnumConst *member = ushim_enum_member(ushim_context_get_result_type(function), memberIndex);
    return member ? member->val.uintVal : 0;
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

static int ushim_dynarray_byte_count(const DynArray *array, int *byteCount)
{
    if (!array || !array->type || !array->type->base || !byteCount)
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
    if (!type || type->kind != TYPE_DYNARRAY || !elementType || elementSize <= 0)
        return 0;

    if (elementType->isGarbageCollected || elementType->size != elementSize)
        return 0;

    return 1;
}

static int ushim_dynarray_type_accepts_strings(const Type *type)
{
    const Type *elementType = ushim_type_element_type(type);
    return type
        && type->kind == TYPE_DYNARRAY
        && elementType
        && elementType->kind == TYPE_STR
        && elementType->size == (int)sizeof(char *);
}

static int ushim_dynarray_type_accepts_nested_bytes(const Type *type, int elementSize)
{
    const Type *outerElementType = ushim_type_element_type(type);
    const Type *innerElementType = ushim_type_nested_dynarray_element_type(type);
    if (!type
        || type->kind != TYPE_DYNARRAY
        || !outerElementType
        || outerElementType->kind != TYPE_DYNARRAY
        || outerElementType->size != (int)sizeof(DynArray)
        || !innerElementType
        || elementSize <= 0)
    {
        return 0;
    }

    if (innerElementType->isGarbageCollected || innerElementType->size != elementSize)
        return 0;

    return 1;
}

static int ushim_dynarray_type_accepts_nested_strings(const Type *type)
{
    const Type *outerElementType = ushim_type_element_type(type);
    const Type *innerElementType = ushim_type_nested_dynarray_element_type(type);
    return type
        && type->kind == TYPE_DYNARRAY
        && outerElementType
        && outerElementType->kind == TYPE_DYNARRAY
        && outerElementType->size == (int)sizeof(DynArray)
        && innerElementType
        && innerElementType->kind == TYPE_STR
        && innerElementType->size == (int)sizeof(char *);
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

static DynArray *ushim_context_result_dynarray(UmkaFuncContext *function)
{
    const Type *type = ushim_context_get_result_type(function);
    UmkaStackSlot *slot = ushim_result(function);
    if (!type || type->kind != TYPE_DYNARRAY || !slot || !slot->ptrVal)
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
        || type->kind != TYPE_DYNARRAY
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
        && type->kind == TYPE_MAP
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
        && type->kind == TYPE_MAP
        && ushim_type_map_key_kind(type) == TYPE_STR
        && !ushim_type_map_value_has_references(type)
        && ushim_type_map_key_size(type) == (int)sizeof(char *)
        && ushim_type_map_value_size(type) > 0
        && valueSize == ushim_type_map_value_size(type);
}

static int ushim_map_type_accepts_string_value_copy(const Type *type, int keySize)
{
    return type
        && type->kind == TYPE_MAP
        && !ushim_type_map_key_has_references(type)
        && ushim_type_map_value_kind(type) == TYPE_STR
        && ushim_type_map_key_size(type) > 0
        && ushim_type_map_value_size(type) == (int)sizeof(char *)
        && keySize == ushim_type_map_key_size(type);
}

static int ushim_map_type_accepts_string_copy(const Type *type)
{
    return type
        && type->kind == TYPE_MAP
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
        && type->kind == TYPE_MAP
        && !ushim_type_map_key_has_references(type)
        && valueType
        && valueType->kind == TYPE_DYNARRAY
        && valueType->size == (int)sizeof(DynArray)
        && elementType
        && !elementType->isGarbageCollected
        && ushim_type_map_key_size(type) > 0
        && keySize == ushim_type_map_key_size(type)
        && elementSize > 0
        && elementSize == elementType->size;
}

static int ushim_map_type_accepts_string_key_dynarray_value_copy(const Type *type, int elementSize)
{
    const Type *valueType = ushim_type_map_value_type(type);
    const Type *elementType = ushim_type_element_type(valueType);
    return type
        && type->kind == TYPE_MAP
        && ushim_type_map_key_kind(type) == TYPE_STR
        && ushim_type_map_key_size(type) == (int)sizeof(char *)
        && valueType
        && valueType->kind == TYPE_DYNARRAY
        && valueType->size == (int)sizeof(DynArray)
        && elementType
        && !elementType->isGarbageCollected
        && elementSize > 0
        && elementSize == elementType->size;
}

static int ushim_map_type_accepts_string_array_value_copy(const Type *type, int keySize)
{
    const Type *valueType = ushim_type_map_value_type(type);
    return type
        && type->kind == TYPE_MAP
        && !ushim_type_map_key_has_references(type)
        && valueType
        && valueType->size == (int)sizeof(DynArray)
        && ushim_dynarray_type_accepts_strings(valueType)
        && ushim_type_map_key_size(type) > 0
        && keySize == ushim_type_map_key_size(type);
}

static int ushim_map_type_accepts_string_key_string_array_value_copy(const Type *type)
{
    const Type *valueType = ushim_type_map_value_type(type);
    return type
        && type->kind == TYPE_MAP
        && ushim_type_map_key_kind(type) == TYPE_STR
        && ushim_type_map_key_size(type) == (int)sizeof(char *)
        && valueType
        && valueType->size == (int)sizeof(DynArray)
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
    if (!type || type->kind != TYPE_MAP || !slot || !slot->ptrVal)
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
    return type && type->isGarbageCollected ? 1 : 0;
}

USHIM_EXPORT const char *ushim_callback_get_parameter_type_name(UmkaStackSlot *params, int index)
{
    return ushim_type_name(ushim_callback_get_parameter_type(params, index));
}

USHIM_EXPORT int ushim_callback_get_parameter_is_enum(UmkaStackSlot *params, int index)
{
    return ushim_enum_type(ushim_callback_get_parameter_type(params, index)) ? 1 : 0;
}

USHIM_EXPORT int ushim_callback_get_parameter_enum_member_count(UmkaStackSlot *params, int index)
{
    const Type *type = ushim_enum_type(ushim_callback_get_parameter_type(params, index));
    return type ? type->numItems : 0;
}

USHIM_EXPORT const char *ushim_callback_get_parameter_enum_member_name(UmkaStackSlot *params, int parameterIndex, int memberIndex)
{
    const EnumConst *member = ushim_enum_member(ushim_callback_get_parameter_type(params, parameterIndex), memberIndex);
    return member ? member->name : NULL;
}

USHIM_EXPORT int64_t ushim_callback_get_parameter_enum_member_signed_value(UmkaStackSlot *params, int parameterIndex, int memberIndex)
{
    const EnumConst *member = ushim_enum_member(ushim_callback_get_parameter_type(params, parameterIndex), memberIndex);
    return member ? member->val.intVal : 0;
}

USHIM_EXPORT uint64_t ushim_callback_get_parameter_enum_member_unsigned_value(UmkaStackSlot *params, int parameterIndex, int memberIndex)
{
    const EnumConst *member = ushim_enum_member(ushim_callback_get_parameter_type(params, parameterIndex), memberIndex);
    return member ? member->val.uintVal : 0;
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
    return type && type->isGarbageCollected ? 1 : 0;
}

USHIM_EXPORT const char *ushim_callback_get_result_type_name(UmkaStackSlot *params, UmkaStackSlot *result)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    return type ? ushim_type_name(type) : "void";
}

USHIM_EXPORT int ushim_callback_get_result_is_enum(UmkaStackSlot *params, UmkaStackSlot *result)
{
    return ushim_enum_type(ushim_callback_get_result_type(params, result)) ? 1 : 0;
}

USHIM_EXPORT int ushim_callback_get_result_enum_member_count(UmkaStackSlot *params, UmkaStackSlot *result)
{
    const Type *type = ushim_enum_type(ushim_callback_get_result_type(params, result));
    return type ? type->numItems : 0;
}

USHIM_EXPORT const char *ushim_callback_get_result_enum_member_name(UmkaStackSlot *params, UmkaStackSlot *result, int memberIndex)
{
    const EnumConst *member = ushim_enum_member(ushim_callback_get_result_type(params, result), memberIndex);
    return member ? member->name : NULL;
}

USHIM_EXPORT int64_t ushim_callback_get_result_enum_member_signed_value(UmkaStackSlot *params, UmkaStackSlot *result, int memberIndex)
{
    const EnumConst *member = ushim_enum_member(ushim_callback_get_result_type(params, result), memberIndex);
    return member ? member->val.intVal : 0;
}

USHIM_EXPORT uint64_t ushim_callback_get_result_enum_member_unsigned_value(UmkaStackSlot *params, UmkaStackSlot *result, int memberIndex)
{
    const EnumConst *member = ushim_enum_member(ushim_callback_get_result_type(params, result), memberIndex);
    return member ? member->val.uintVal : 0;
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

static DynArray *ushim_callback_param_dynarray(UmkaStackSlot *params, int index)
{
    const Type *type = ushim_callback_get_parameter_type(params, index);
    UmkaStackSlot *slot = ushim_param(params, index);
    if (!type || type->kind != TYPE_DYNARRAY || !slot)
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
        || type->kind != TYPE_DYNARRAY
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
    if (!type || type->kind != TYPE_MAP || !slot)
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

USHIM_EXPORT int ushim_callback_set_result_data(UmkaStackSlot *params, UmkaStackSlot *result, const void *value, int size)
{
    const Type *type = ushim_callback_get_result_type(params, result);
    UmkaStackSlot *slot = umkaGetResult(params, result);
    if (!type || !slot || !slot->ptrVal || !value || size != type->size || type->isGarbageCollected)
        return 1;

    memcpy(slot->ptrVal, value, size);
    return 0;
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
