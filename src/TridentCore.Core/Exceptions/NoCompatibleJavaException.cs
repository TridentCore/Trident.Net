namespace TridentCore.Core.Exceptions;

// 需求集为空：该实例的全部 patch 声明取交集后没有任何 Java 主版本剩下。
public class NoCompatibleJavaException()
    : Exception("No compatible Java major remains for this instance; the applied patches narrowed the set to nothing.");
