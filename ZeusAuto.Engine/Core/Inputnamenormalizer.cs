namespace ZeusAuto.Engine.Core;

// ─────────────────────────────────────────────────────────────────────────────
//  InputNameNormalizer — tabela de aliases de botão, fonte única da verdade
//
//  Antes: a mesma tabela de aliases existia duplicada em MacroEngine e em
//  MouseSimulator. Qualquer novo alias (ex: "TECLA XBUTTON6") precisava ser
//  adicionado nos dois lugares — risco de divergência silenciosa.
//
//  Agora: única implementação, consumida por ambas as classes via método
//  estático. Sem alocação extra — método puro sobre string.
// ─────────────────────────────────────────────────────────────────────────────

public static class InputNameNormalizer
{
    /// <summary>
    /// Normaliza um nome de botão para a forma canônica usada internamente
    /// ("MouseLeft", "MouseRight", "MouseMiddle", "MouseX1", "MouseX2").
    ///
    /// Aceita qualquer capitalização e os aliases em PT-BR definidos na spec.
    /// Retorna a string original (trimada, uppercase) se não houver mapeamento —
    /// nunca retorna null.
    /// </summary>
    public static string Normalize(string? inputName) =>
        inputName?.Trim().ToUpperInvariant() switch
        {
            "MOUSELEFT"   or "LEFT"   or "TECLA ESQUERDA"        => "MouseLeft",
            "MOUSERIGHT"  or "RIGHT"  or "TECLA DIREITA"         => "MouseRight",
            "MOUSEMIDDLE" or "MIDDLE" or "TECLA SCROLL"          => "MouseMiddle",
            "MOUSEX1" or "X1" or "XBUTTON1" or "TECLA XBUTTON4" => "MouseX1",
            "MOUSEX2" or "X2" or "XBUTTON2" or "TECLA XBUTTON5" => "MouseX2",
            var v => v ?? string.Empty
        };
}