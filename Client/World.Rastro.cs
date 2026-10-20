using Godot;

namespace Jandirus.Client;

public partial class World
{
	/// <summary>
	/// (2026-10-19) ONDE ESTA, NO MEU SNAPSHOT, O UNICO CORPO COM ESTE NOME -- a precisao da seta do rastro do Sense
	/// (`RastroNaTela`). Nulo se ninguem, ou mais de um, tem o nome (NPC se repete: "Alma", "Habitante" -- a seta nao escolhe por
	/// sorte), e nulo pra mim mesmo.
	///
	/// NAO E INFORMACAO NOVA: o snapshot ja traz a zona inteira (menos os ocultos) e e com ele que o corpo e desenhado. E o rastro so
	/// pergunta por aqui com o nome que a LISTA do Sense trouxe -- e ela so traz o nome de quem voce conhece (`IdentidadePara`).
	/// A POSICAO E A DO CHAO (`GlobalPosition`), e nao a desenhada: quem voa sobe 160 px na tela sem sair do lugar no mapa.
	/// </summary>
	internal Vector2? PosicaoDoUnicoComONome(string nome)
	{
		if (nome.Length == 0 || nome == GameClient.Instance?.LocalName) return null;
		RemotePlayer? achado = null;
		foreach ((int id, string n) in _nomes)
		{
			if (n != nome || !_remotos.TryGetValue(id, out RemotePlayer? r) || !IsInstanceValid(r)) continue;
			if (achado != null) return null;
			achado = r;
		}
		return achado?.GlobalPosition;
	}
}
