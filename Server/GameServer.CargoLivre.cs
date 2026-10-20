using System.Text.Json;
using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS CARGOS SEM ADMIN (dono, 2026-10-19): *"fazer com q todos os cargos do jogo tenham requisitos e n precisem de adm (tirando o
/// anjo)"*. A regra mora no Core (`Cargos`, `AbandonoDeCargo`); aqui ficam as tres coisas que so o servidor sabe:
///   * QUAIS PLANETAS PRE-FEITOS a alma domina (o King of Acronia pede Arconia -- a contagem do `QuantosDomina` nao diz qual);
///   * se VEGETA ESTA CONDENADO (o Rei NPC nao renasce, e o trono vago do Rei ganha a porta de requisito);
///   * a AUSENCIA de cada dono de cargo, somada so com o servidor no ar, que vaga o trono de quem sumiu ha 7 dias.
/// </summary>
public partial class GameServer
{
	/// <summary>Os planetas PRE-FEITOS que esta assinatura domina, pelo nome da zona ("Arconia"). Mundo sorteado nao entra: cargo nenhum o pede.</summary>
	private List<string> PreFeitosQueDomina(string assinatura) =>
		[.. DominiosDe(assinatura).Where(d => d.PreFeito).Select(d => d.Planeta)];

	/// <summary>Vegeta esta condenado (destruido, ou no pavio): o Rei NPC nao renasce la -- ver `GameServer.Povoamento.cs`.</summary>
	private bool VegetaCondenado => ZonaCondenada(ZoneKey.Premade(MissoesDeCargo.PlanetaDoCargo(Sucessao.ReiDeVegeta)));

	// =====================================================================
	// O ABANDONO -- a ausencia do dono, somada com o servidor no ar
	// =====================================================================

	/// <summary>A ausencia de quem esta numa vaga: a conta (a vaga troca de dono, a conta zera) e quanto tempo de servidor no ar ela nao veio.</summary>
	private sealed class Ausencia
	{
		public string Conta = "";
		public long Ms;
	}

	/// <summary>Vaga (`kaioshin#2`, `turtle`) -> a ausencia do dono dela. Em disco, ao lado do `cargos.txt`.</summary>
	private readonly Dictionary<string, Ausencia> _ausencias = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Quando foi a ultima volta (ms Unix). Zero depois do boot: a primeira volta so ancora, e nao soma nada.</summary>
	private long _voltaDoAbandono;

	private string CaminhoDasAusencias => System.IO.Path.Combine(_store?.Pasta ?? ".", "cargos-ausencia.json");

	/// <summary>Le as ausencias do disco. Chamado junto com os cargos (`CarregarCargos`), no boot e em toda subida.</summary>
	private void CarregarAusencias()
	{
		_ausencias.Clear();
		_voltaDoAbandono = 0;
		try
		{
			if (System.IO.File.Exists(CaminhoDasAusencias)
				&& JsonSerializer.Deserialize<Dictionary<string, Ausencia>>(System.IO.File.ReadAllText(CaminhoDasAusencias),
					new JsonSerializerOptions { IncludeFields = true }) is { } lidas)
				foreach ((string vaga, Ausencia a) in lidas) _ausencias[vaga] = a;
		}
		catch (Exception e) { GD.PushWarning($"[server] cargos-ausencia.json ilegivel: {e.Message}"); }
	}

	private void SalvarAusencias()
	{
		if (_store == null) return;
		try
		{
			// TEMPORARIO E RENOMEIA, como o `missoes-de-cargo.json`.
			string tmp = CaminhoDasAusencias + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_ausencias, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDasAusencias, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar cargos-ausencia.json: {e.Message}"); }
	}

	/// <summary>Chamado no tique de 1 Hz dos cargos (`TickDosCargos`); a volta de verdade sai a cada <see cref="AbandonoDeCargo.MsEntreVoltas"/>.</summary>
	private void TickDoAbandono()
	{
		long agora = NowMs();
		if (_voltaDoAbandono > 0 && agora - _voltaDoAbandono < AbandonoDeCargo.MsEntreVoltas) return;
		VoltaDoAbandono(agora);
	}

	/// <summary>
	/// UMA VOLTA: cada dono offline soma o passo (o tempo desde a volta anterior, com teto); dono online zera; a ausencia que venceu
	/// vaga o trono pelo funil de sempre (`Destronar`, que anuncia ao mundo e tira o kit). O Anjo fica de fora.
	/// </summary>
	private void VoltaDoAbandono(long agora)
	{
		long passo = AbandonoDeCargo.Passo(_voltaDoAbandono, agora);
		_voltaDoAbandono = agora;
		bool mexeu = false;

		// as vagas que nao tem mais dono saem do livro (o trono vagou por outra porta)
		foreach (string vaga in _ausencias.Keys.Where(v => !_tronos.ContainsKey(v)).ToList()) { _ausencias.Remove(vaga); mexeu = true; }

		foreach ((string vaga, string conta) in _tronos.ToList())
		{
			string cargo = CargoDaVaga(vaga);
			if (!AbandonoDeCargo.Vaga(cargo)) continue;
			if (!_ausencias.TryGetValue(vaga, out Ausencia? a) || !string.Equals(a.Conta, conta, StringComparison.OrdinalIgnoreCase))
			{
				a = new Ausencia { Conta = conta };
				_ausencias[vaga] = a;
				mexeu = true;
			}
			if (OnlinePorConta(conta) != null)
			{
				if (a.Ms != 0) { a.Ms = 0; mexeu = true; }
				continue;
			}
			if (passo > 0) { a.Ms += passo; mexeu = true; }
			if (!AbandonoDeCargo.Vence(a.Ms)) continue;

			_ausencias.Remove(vaga);
			Destronar(cargo, $"o dono sumiu: {AbandonoDeCargo.DiasSemEntrar} dias sem entrar no jogo", conta);
		}
		if (mexeu) SalvarAusencias();
	}
}
