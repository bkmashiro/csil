using System.Reflection;
using System.Reflection.Emit;

namespace CSTrail
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // 创建目标对象实例
            IMyInterface target = new MyClass();

            // 创建拦截器实例
            IInterceptor interceptor = new MyInterceptor();

            // 创建动态代理对象
            IMyInterface proxy = MyProxyGenerator.CreateInterfaceProxyWithTarget(target, interceptor);

            // 调用代理对象的方法
            var ret = proxy.MyMethod("aaa", 1234);
            Console.WriteLine(ret);
            //Console.ReadLine();
        }
    }



    public interface IMyInterface
    {
        string MyMethod(string foo, int bar);
    }

    public class MyInterceptor : IInterceptor
    {
        public void BeforeInvoke()
        {
            Console.WriteLine("Before invoking method");
        }

        public void AfterInvoke()
        {
            Console.WriteLine("After invoking method");
        }
    }

#pragma warning disable CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8603 // 可能返回 null 引用。
#pragma warning disable CS8604 // 引用类型参数可能为 null。

    public class MyProxyGenerator
    {
        public static T CreateInterfaceProxyWithTarget<T>(T target, IInterceptor interceptor) where T : class
        {
            // 创建代理类，实现指定接口
            Type proxyType = CreateProxyType<T>();

            // 创建代理对象
            T proxy = Activator.CreateInstance(proxyType) as T;

            // 将目标对象和拦截器绑定到代理对象上
            SetTarget(proxy, target);
            SetInterceptor(proxy, interceptor);

            return proxy;
        }

        private static Type CreateProxyType<T>()
        {
            // 创建动态程序集
            AssemblyName assemblyName = new AssemblyName("MyProxyAssembly");
            AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("MyDynamicModule");

            // 创建代理类
            TypeBuilder typeBuilder = moduleBuilder.DefineType($"{typeof(T).Name}Proxy", TypeAttributes.Public, typeof(DispatchProxy), new Type[] { typeof(T) });
            // 添加私有字段
            FieldBuilder targetField = typeBuilder.DefineField("__target", typeof(T), FieldAttributes.Private);
            FieldBuilder interceptorField = typeBuilder.DefineField("__interceptor", typeof(IInterceptor), FieldAttributes.Private);
            // override Invoke method
            MethodBuilder invokeMethod = typeBuilder.DefineMethod("Invoke", MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig, typeof(object), new Type[] { typeof(MethodInfo), typeof(object[]) });
            ILGenerator il = invokeMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, typeof(MyProxyGenerator).GetMethod(nameof(InvokeMethod), BindingFlags.Public | BindingFlags.Static));
            il.Emit(OpCodes.Ret);
            
            // implement interface methods
            foreach (MethodInfo methodInfo in typeof(T).GetMethods())
            {
                var p = methodInfo.GetParameters().Select(p => p.ParameterType).ToArray();
                MethodBuilder methodBuilder = typeBuilder.DefineMethod(methodInfo.Name, MethodAttributes.Public | MethodAttributes.Virtual, methodInfo.ReturnType, 
                    methodInfo.GetParameters().Select(p => p.ParameterType).ToArray());
                il = methodBuilder.GetILGenerator();
                // load methodInfo
                il.Emit(OpCodes.Ldtoken, methodInfo);
                il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", new Type[] { typeof(RuntimeMethodHandle) }));
                il.Emit(OpCodes.Castclass, typeof(MethodInfo));
                // load this
                il.Emit(OpCodes.Ldarg_0);
                // create object[] for arguments
                LocalBuilder arr = il.DeclareLocal(typeof(object[]));
                int nargs = methodInfo.GetParameters().Length;
                il.Emit(OpCodes.Ldc_I4, nargs); // 将参数个数推入栈顶
                il.Emit(OpCodes.Newarr, typeof(object)); // 创建一个 object[] 数组
                il.Emit(OpCodes.Stloc, arr); // 将数组引用存储到本地变量 argsArray 中
                for (int i = 0; i < nargs; i++)
                {
                    // 将数组引用加载到栈顶
                    il.Emit(OpCodes.Ldloc, arr);
                    // 将数组索引推入栈顶
                    il.Emit(OpCodes.Ldc_I4, i);
                    // 将参数值加载到栈顶
                    ParameterInfo paramInfo = methodInfo.GetParameters()[i];
                    Type paramType = paramInfo.ParameterType;
                    if (paramType.IsValueType)
                    {
                        il.Emit(OpCodes.Ldarg, i + 1); // 加载参数值到栈顶
                        il.Emit(OpCodes.Box, paramType); // 执行装箱操作
                    }
                    else
                    {
                        // 对于引用类型参数，直接加载参数值到栈顶
                        il.Emit(OpCodes.Ldarg, i + 1);
                    }
                    il.Emit(OpCodes.Stelem_Ref);
                }

                il.Emit(OpCodes.Ldloc, arr);
                il.Emit(OpCodes.Call, typeof(MyProxyGenerator)
                    .GetMethod(nameof(InvokeMethod), BindingFlags.Public | BindingFlags.Static));
                 
                il.Emit(OpCodes.Ret);
            }
            
            // 创建代理类
            Type proxyType = typeBuilder.CreateType();

            return proxyType;
        }

        private static void SetTarget<T>(T proxy, T target)
        {
            FieldInfo targetField = proxy.GetType().GetField("__target", BindingFlags.Instance | BindingFlags.NonPublic);
            targetField.SetValue(proxy, target);
        }

        private static void SetInterceptor<T>(T proxy, IInterceptor interceptor)
        {
            FieldInfo interceptorField = proxy.GetType().GetField("__interceptor", BindingFlags.Instance | BindingFlags.NonPublic);
            interceptorField.SetValue(proxy, interceptor);
        }

        public static object InvokeMethod(MethodInfo method, object proxy, object[] args)
        {
            IInterceptor interceptor = GetInterceptor(proxy);
            interceptor.BeforeInvoke();
            object result = method.Invoke(GetTarget(proxy), args);
            interceptor.AfterInvoke();

            return result;
        }

        private static T GetTarget<T>(T proxy)
        {
            FieldInfo targetField = proxy.GetType().GetField("__target", BindingFlags.Instance | BindingFlags.NonPublic);
            return (T)targetField.GetValue(proxy);
        }

        private static IInterceptor GetInterceptor<T>(T proxy)
        {
            FieldInfo interceptorField = proxy.GetType().GetField("__interceptor", BindingFlags.Instance | BindingFlags.NonPublic);
            return (IInterceptor)interceptorField.GetValue(proxy);
        }
    }
    public class MyClass : IMyInterface
    {
        public string MyMethod(string foo, int bar)
        {
            Console.WriteLine($"Executing MyMethod in MyClass, {foo}, {bar}");
            return "123";
        }
    }

    public interface IInterceptor
    {
        void BeforeInvoke();
        void AfterInvoke();
    }
}

