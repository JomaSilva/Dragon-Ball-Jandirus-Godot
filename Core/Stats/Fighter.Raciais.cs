using System.Text.Json.Serialization;

namespace Jandirus.Core.Stats;

/// <summary>
/// ============================ O CANAL RACIAL (v0.4.0) ============================
/// O que as passivas e ativas raciais fazem na ficha (`Races.EstadoRacial` -> `Races.EfeitoRacial`). Irmao do canal `forma*` e pela
/// mesma regra: **ESCRITOS, NUNCA ACUMULADOS** -- o servidor (`GameServer.Passivas`) os afirma a cada tique do efeito de agora, e
/// nao ha "desfazer" pra dar errado.
///
/// **FORA DO SAVE** (`JsonIgnore`): e a licao do "buff nao vai pro save" -- um fator gravado volta no login valendo, antes de qualquer
/// tique o reescrever, e um buff de 30% preso no disco e um personagem 30% mais forte pra sempre. Quem volta do disco volta neutro.
///
/// AS RECARGAS SAO O CONTRARIO, E VAO PRO SAVE: deslogar nao pode ser o jeito de zerar a recarga de 20 min do Demonic Saiyan.
/// ===================================================================================
/// </summary>
public sealed partial class Fighter
{
	/// <summary>Multiplica o BP na familia 1 (`tempBP *= BPBoost * formBuff * racialBp`) -- e onde mora tambem a SOMA na forma (False
	/// Advantage, Prodigy), que o servidor converte em razao sobre o multiplicador da forma de agora.</summary>
	[JsonIgnore] public double racialBp = 1;

	/// <summary>Os stats: fator do R inteiro no `Statify`, no mesmo posto do `forma*`.</summary>
	[JsonIgnore] public double racialPhysoff = 1, racialPhysdef = 1, racialSpeed = 1, racialKioff = 1, racialKidef = 1, racialKiskill = 1,
		racialTecnica = 1;

	/// <summary>A regeneracao de Ki parado (`RegenDeKi`), a ATIVA (a tecla C e a meditacao) e a do vigor (a digestao).</summary>
	[JsonIgnore] public double racialRegenKiPassiva = 1, racialRegenKiAtiva = 1, racialRegenVigor = 1;

	/// <summary>O teto de Ki (`MaxKi`) e o teto da carga (o `powerupcap`, e com ele o teto duro de 200%).</summary>
	[JsonIgnore] public double racialMaxKi = 1, racialTetoDaCarga = 1;

	/// <summary>O CONSUMO de Ki das tecnicas (`BaseDrain`, o fator de custo de toda tecnica) e a VELOCIDADE da carga de tecnica
	/// (`Projetil.SegundosDeCarga`: 2 = carrega na metade do tempo).</summary>
	[JsonIgnore] public double racialCustoKi = 1, racialCargaDeTecnica = 1;

	/// <summary>O ganho de TREINO (`TrainGain`): o Persistence do Humano, ate 3x.</summary>
	[JsonIgnore] public double racialGanhoDeTreino = 1;

	/// <summary>As recargas das ativas raciais (e de tres passivas), em ms absolutos -- ver `Races.EstadoRacial.FaltaDaRecarga`.</summary>
	public Dictionary<string, long> RecargasRaciais = new(StringComparer.Ordinal);
}
