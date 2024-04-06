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
            var ret = proxy.MyMethod("aaa", "bar");
            Console.WriteLine(ret);
            //Console.ReadLine();
        }
    }



    public interface IMyInterface
    {
        string MyMethod(string foo, object bar);
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
            /*
             we want to create a type like this:
             class FooProxy : DispatchProxy
             {
                 private object target;

                 public void SetTarget(object target)
                 {
                     this.target = target;
                 }

                 protected override object Invoke(MethodInfo targetMethod, object[] args)
                 {
                     return targetMethod.Invoke(target, args);
                 }
             }
             */

            // 创建动态程序集
            AssemblyName assemblyName = new AssemblyName("MyProxyAssembly");
            AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("MyDynamicModule");

            // 创建代理类
            TypeBuilder typeBuilder = moduleBuilder.DefineType($"{typeof(T).Name}Proxy", TypeAttributes.Public, typeof(DispatchProxy), [typeof(T)]);
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
            il.Emit(OpCodes.Call, typeof(MyProxyGenerator).GetMethod("InvokeMethod", BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(typeof(T)));
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
                //TODO : load arguments here
                // create object[] for arguments
                //il.Emit(OpCodes.Newarr, typeof(object));
                //LocalBuilder arrayLocal = il.DeclareLocal(typeof(object[]));
                //il.Emit(OpCodes.Stloc, arrayLocal);
                //il.Emit(OpCodes.Ldloca, arrayLocal);
                il.Emit(OpCodes.Call, typeof(MyProxyGenerator)
                    .GetMethod("PrintMethod", BindingFlags.Public | BindingFlags.Static));
                 
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

        public static object PrintMethod(object method, object that, object args)
        {
            Console.WriteLine(method);
            Console.WriteLine(that);
            Console.WriteLine(args);
            return null;
        }

        public static object InvokeMethod<T>(MethodInfo methodInfo, object[] allargs)
        {
            T proxy = (T)allargs[0];
            object[] args = allargs.Skip(1).ToArray();
            IInterceptor interceptor = GetInterceptor(proxy);
            interceptor.BeforeInvoke();
            Console.WriteLine(111);
            object result = methodInfo.Invoke(GetTarget(proxy), args);
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
        public string MyMethod(string foo, object bar)
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

