# CSIL
使用 IL weaving 在运行时拦截C#方法调用, 实现拦截器的功能.

``` cs
            public class MyClass : IMyInterface
            {
                public string MyMethod(string foo, int bar)
                {
                    Console.WriteLine($"Executing MyMethod in MyClass, {foo}, {bar}");
                    return "123";
                }
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

            // 创建目标对象实例
            IMyInterface target = new MyClass();

            // 创建拦截器实例
            IInterceptor interceptor = new MyInterceptor();

            // 创建动态代理对象
            IMyInterface proxy = MyProxyGenerator.CreateInterfaceProxyWithTarget(target, interceptor);

            // 调用代理对象的方法
            var ret = proxy.MyMethod("aaa", 1234);
            Console.WriteLine(ret);
```

Output:
``` plaintext
Before invoking method
Executing MyMethod in MyClass, aaa, 1234
After invoking method
123
```


## IL Weaving
动态生成代理类, 劫持原方法.
生成与原类实现相同接口的代理类, 覆盖所有方法为携带原调用的`methodInfo`, `this` 和 `params` 调用劫持方法 `InvokeMethod(MethodInfo method, object proxy, object[] args) `

覆盖的方法:
+ 加载`methodInfo`入栈
+ 将第一个参数(`this`)压栈
+ 创建`object[]`数组`arr`用于存储原参数列表
+ 循环将原参数压入`arr`, 引用类型直接存入引用, 值类型装箱然后存入
+ 调用劫持方法`InvokeMethod` (在此方法内调用`Interceptor`代码, 然后调用原方法)
+ `InvokeMethod` 返回原方法返回值到栈顶
+ 返回返回值.


``` cs
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
                    if (paramType.IsValueType) //如果是数值类型, 装箱
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
```
