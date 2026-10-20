using Godot;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-19) A CORRENTE DE PODER DO DU NO MUNDO -- quem recebe o folego (`GameClient.DandoPoder`) e o entrega a corrente daquele
/// doador, criando-a se ela nao existe. O desenho e o porque estao em <see cref="CorrenteDePoder"/>; o fio, em
/// `GameServer.CorrenteDePoder.cs`.
/// </summary>
public partial class World
{
	/// <summary>As correntes de pe, por doador: um folego novo renova a que ja existe (o molde dos `_choques` do embate de ki).</summary>
	private readonly Dictionary<int, CorrenteDePoder> _correntesDePoder = [];

	private void AoDarPoder(int doador, int alvo)
	{
		if (CorrenteDePoder.ApagadaDeTeste) return;
		if (!_correntesDePoder.TryGetValue(doador, out CorrenteDePoder? c) || !IsInstanceValid(c) || c.Encerrando)
		{
			c = new CorrenteDePoder(doador, OndeEstaDesenhado) { Name = $"CorrenteDePoder{doador}" };
			_correntesDePoder[doador] = c;
			_atores.AddChild(c);
		}
		c.Folego(alvo);
	}

	/// <summary>Onde o corpo deste id esta DESENHADO (com a altura do voo), ou nulo se ele nao esta na tela.</summary>
	private Vector2? OndeEstaDesenhado(int id) => Corpo(id) is { } corpo ? Desenhado(corpo) : null;

	/// <summary>So pra bancada (`--diagcorrente`): onde o corpo deste id esta desenhado, ou nulo se ele nao esta na tela.</summary>
	internal Vector2? DesenhadoDeTeste(int id) => OndeEstaDesenhado(id);

	/// <summary>So pra bancada: a corrente de pe deste doador (nula se nao ha, ou se ja esta saindo da arvore).</summary>
	internal CorrenteDePoder? CorrenteDeTeste(int doador) =>
		_correntesDePoder.TryGetValue(doador, out CorrenteDePoder? c) && IsInstanceValid(c) && !c.Encerrando ? c : null;
}
