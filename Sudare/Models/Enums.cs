namespace Sudare.Models;

/// <summary>パターンの解釈方法。</summary>
public enum MatchMode
{
    /// <summary>単純な部分一致。</summary>
    Plain,

    /// <summary><c>*</c> と <c>?</c> のみを特別扱いする部分一致。</summary>
    Wildcard,

    /// <summary>.NET 正規表現。</summary>
    Regex,
}

/// <summary>複数パターンの結合方法。</summary>
public enum LogicMode
{
    /// <summary>いずれかに一致。</summary>
    Or,

    /// <summary>すべてに一致。</summary>
    And,
}
