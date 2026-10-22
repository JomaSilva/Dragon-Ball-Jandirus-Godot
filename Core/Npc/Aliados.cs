using Jandirus.Core.Tech;

namespace Jandirus.Core.Npc;

/// <summary>
/// UM ALIADO -- o habitante que um personagem recrutou (2026-10-21, ver <see cref="Amizade"/>). Mora na FICHA do personagem
/// (`Fighter.Aliados`), entao vai pro save sozinho e some junto quando o personagem e apagado ou a vaga e reciclada.
///
/// O QUE ELE E, NO JOGO, E A CRIATURA DE UMA CAPSULA (decisao do dono: *"vira companheiro"*): a <see cref="Companheiro"/> de dentro
/// e o mesmo registro que a Pee-Kay Ball guarda no cofre (nome, raca, cara, poder, nivel, relacao, melhorias, reflexos), e solto ele
/// anda no MESMO motor (`GameServer.Companheiros.cs`: segue, fica, ataca, mata, sobe de nivel, volta pra "casa" quando o dono sai da
/// zona ou do jogo). O que muda e de onde ele vem (a amizade, e nao a captura), onde mora (a ficha, e nao o cofre de tech), por onde
/// recebe ordens (a aba Allies, e nao a capsula) -- e a MORTE, que e definitiva: ele morre como o habitante que era.
/// </summary>
public sealed class Aliado
{
	/// <summary>O numero dele entre os aliados DESTE personagem (`Fighter.ProximoAliado`) -- e por ele que a aba manda as ordens.</summary>
	public int Id;

	/// <summary>O registro do motor do companheiro: nome, raca, cara, poder, nivel, relacao, melhorias e reflexos.</summary>
	public Companheiro Criatura = new();

	/// <summary>O planeta em que ele morava (so pra aba).</summary>
	public string Planeta = "";

	/// <summary>Quando foi recrutado (Unix ms).</summary>
	public long RecrutadoEm;
}

/// <summary>
/// OS NUMEROS E AS ORDENS DOS ALIADOS. A regra de como um aliado nasce do habitante mora aqui; o resto (soltar, ordenar, morrer) e do
/// servidor (`GameServer.Aliados.cs`).
/// </summary>
public static class Aliados
{
	/// <summary>
	/// QUANTOS ALIADOS um personagem tem no maximo, soltos ou guardados (decisao do orquestrador: uma constante, ex. 6). Soltos ao mesmo
	/// tempo sao os do motor da capsula: <see cref="Companheiros.MaxSoltos"/>, contando JUNTO com as criaturas de capsula do dono.
	/// </summary>
	public const int Maximo = 6;

	/// <summary>
	/// A INTELIGENCIA DE UM ALIADO: a que entende todas as ordens de luta da capsula -- seguir/ficar e atacar (1), matar (2) e os dois
	/// reflexos (3 e 4, que pedem ainda a relacao acima de 20). Decisao minha: e gente, e a decisao do dono lista "kill" entre as ordens.
	/// </summary>
	public const int InteligenciaDoAliado = Companheiros.IntDeQuandoApanho;

	/// <summary>A "especie" do registro do motor: nenhuma das capsulas de fabrica (o corpo nasce com o ki padrao do motor).</summary>
	public const string Especie = "Aliado";

	/// <summary>O nome que a aba e os robos procuram.</summary>
	public const string NomeDaAba = "Allies";

	// ---- as ordens da aba (o argumento e o `Aliado.Id`) ----
	public const string VerboSoltar = "aliado_soltar", VerboGuardar = "aliado_guardar", VerboSeguir = "aliado_seguir",
						VerboAtacar = "aliado_atacar", VerboMatar = "aliado_matar", VerboParar = "aliado_parar",
						VerboQuandoAtaco = "aliado_quando_ataco", VerboQuandoApanho = "aliado_quando_apanho",
						VerboMelhorar = "aliado_melhorar", VerboDispensar = "aliado_dispensar";

	/// <summary>
	/// O ALIADO QUE NASCE DE UM HABITANTE: o nome, a raca, o genero, a cara e o poder dele; nivel 1; a inteligencia de gente; e a RELACAO
	/// comeca na amizade que o trouxe (o laco cheio) -- e com ela acima de 20 os dois reflexos ja abrem, como na criatura de capsula que
	/// conviveu muito tempo com o dono.
	/// </summary>
	public static Aliado DoHabitante(int id, string nome, string raca, string genero, Appearance.Appearance visual, double bp,
									 string planeta, long agoraMs, double relacao) => new()
	{
		Id = id,
		Planeta = planeta,
		RecrutadoEm = agoraMs,
		Criatura = new Companheiro
		{
			Especie = Especie,
			Nome = nome,
			Raca = raca,
			Genero = genero,
			Visual = visual,
			Bp = Math.Max(bp, 1),
			Inteligencia = InteligenciaDoAliado,
			Relacao = relacao,
		},
	};
}
