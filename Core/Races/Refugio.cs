using Jandirus.Core.World;

namespace Jandirus.Core.Races;

/// <summary>
/// POR QUE este corpo foi parar aqui quando o berco dele deixou de existir.
///
/// Vai pro chat do jogador e pra bancada. Ele nao e enfeite: as quatro respostas sao caminhos
/// DIFERENTES do <see cref="Refugios"/>, e a unica forma de a bancada afirmar "esta pessoa caiu no
/// ultimo recurso" e o motivo dizer isso -- do contrario um refugio que sempre caisse no vacuo
/// ficaria verde por parecer com um refugio que sempre achasse mundo.
/// </summary>
public enum MotivoDoRefugio : byte
{
	/// <summary>
	/// Um planeta que ESTE personagem conquistou -- a opcao B1 do pedido do dono.
	///
	/// **Nao ha um `Nenhum = 0` aqui de proposito.** Este enum so existe DENTRO do refugio, e la
	/// dentro sempre aconteceu alguma coisa: chegar a ele ja quer dizer que o berco morreu. Um valor
	/// "nada" seria um estado que nenhum caminho produz -- e valor que ninguem escreve e valor que a
	/// proxima pessoa usa errado.
	/// </summary>
	Dominio = 0,

	/// <summary>Um mundo vivo perto do natal -- a opcao B2. O caso comum.</summary>
	PertoDoNatal = 1,

	/// <summary>
	/// A RESERVA: perto do natal so havia mundo pesado demais pra um recem-nascido
	/// (<see cref="Bercos.GravidadeMaximaDeBerco"/>), e um mundo pesado e melhor que lugar nenhum.
	/// Mesma decisao (e mesmo argumento) da reserva do <see cref="Bercos.Exilio"/>.
	/// </summary>
	MundoPesado = 2,

	/// <summary>
	/// O ULTIMO RECURSO: nao havia dominio nem vizinhanca, e o corpo abre os olhos no ESPACO ABERTO,
	/// na coordenada de onde o natal ficava. Ver <see cref="Refugios"/>.
	/// </summary>
	OEspacoAberto = 3,
}

/// <summary>
/// UM CANDIDATO A REFUGIO -- o corpo, o ENDERECO dele e a que distancia do natal ele esta.
///
/// ============================ O ENDERECO VIAJA JUNTO, E NAO E LUXO ============================
/// O <see cref="Berco"/> abre explicando que o nome NAO e chave e o endereco (Sx, Sy, K) e -- os
/// nomes gerados colidem porque o padrao `$"{bioma}-{|Sx|%1000}{|Sy|%1000}{k}"` perde o sinal da
/// celula. Um refugio que devolvesse so o `PlanetaNoEspaco` obrigaria quem o recebe a procurar de
/// novo em que orbita aquele corpo estava, e e essa segunda busca que erra: sem `K >= 0` o
/// <see cref="Berco.NoEspaco"/> devolve nulo e o corpo nao consegue nem ser posto em orbita.
///
/// Aqui o endereco sai de graca -- a varredura ja esta dentro do sistema quando encontra o corpo.
/// ==========================================================================================
/// </summary>
public readonly record struct MundoDeRefugio(
	PlanetaNoEspaco Corpo, int Sx, int Sy, int K, double Distancia, bool ServeDeBerco);

/// <summary>
/// O QUE HA PERTO DE UM PONTO DO UNIVERSO -- o resultado de <see cref="Refugios.MundosPertoDe"/>.
/// </summary>
public readonly record struct Arredores
{
	/// <summary>Os candidatos, do MAIS PERTO pro mais longe. Vazia quer dizer "nao ha vizinhanca".</summary>
	public IReadOnlyList<MundoDeRefugio> Mundos { get; init; }

	/// <summary>
	/// Nenhum mundo passou no crivo de gravidade e estes sao os PESADOS. Ver
	/// <see cref="MotivoDoRefugio.MundoPesado"/> -- e a mesma reserva do exilio do Lendario.
	/// </summary>
	public bool Reserva { get; init; }

	/// <summary>
	/// QUANTAS CELULAS DE SISTEMA A BUSCA OLHOU. Lido pelo log do refugio -- e o numero que responde
	/// *"por que esta pessoa foi parar no vacuo"* meses depois, e o que denuncia a busca desligada
	/// (zero celulas). Irmao do <see cref="Berco.CelulasOlhadas"/>, e pelo mesmo motivo: um esforco
	/// que ninguem consegue ver e um esforco que ninguem consegue afirmar.
	/// </summary>
	public int CelulasOlhadas { get; init; }

	/// <summary>A celula em que a ancora caiu -- e o sal do sorteio, ver <see cref="Sorteia"/>.</summary>
	public SistemaId Celula { get; init; }

	public bool Vazia => Mundos == null || Mundos.Count == 0;

	/// <summary>
	/// QUAL DELES SAI, pra ESTE personagem.
	///
	/// ============================ O JOGADOR ESCOLHE A OPCAO, NAO O MUNDO ============================
	/// A tela da criacao ja resolveu esta mesma pergunta e o comentario dela diz por que
	/// (<see cref="Bercos.IrmasDoNatal"/>): a gravidade do berco vira `GravMastered` de graca
	/// (`race.dm:130-131`), entao deixar o jogador apontar o mundo seria deixa-lo escolher um
	/// atributo permanente. Com o refugio isso pesaria MAIS, nao menos -- ele e automatico e chega
	/// numa catastrofe, ou seja, seria uma vantagem distribuida por desastre.
	///
	/// Entao o jogador escolhe ENTRE AS DUAS OPCOES do pedido (o dominio dele ou a vizinhanca de
	/// casa); QUAL mundo da vizinhanca e sorteio, e o sorteio e o mesmo da <see cref="Bercos.Vizinho"/>:
	/// funcao pura da semente do personagem misturada com a celula, sem `Random` nenhum, pra que a
	/// mesma pessoa caia sempre no mesmo lugar em toda maquina e em todo reinicio.
	/// ==========================================================================================
	/// </summary>
	public MundoDeRefugio? Sorteia(ulong seedDoPersonagem)
	{
		if (Vazia) return null;

		ulong h = Espaco.Misturar(seedDoPersonagem ^ Refugios.SalDoRefugio,
								  (ulong)(uint)Celula.Sx, (ulong)(uint)Celula.Sy);
		return Mundos[(int)(h % (ulong)Mundos.Count)];
	}
}

/// <summary>
/// **O REFUGIO: PARA ONDE VAI QUEM PERDEU O PLANETA NATAL.** Funcao PURA da carta estelar.
///
/// ============================ O PEDIDO DO DONO, LITERAL ============================
/// *"quando uma raca fica sem planeta natal, o jogador pode ou spawnar em um planeta q ele
/// conquistou ou em um planeta proximo do planeta natal dele"*.
///
/// Sao DUAS opcoes e nao uma cascata: **B1** um mundo que ELE conquistou, **B2** um mundo PERTO do
/// natal. Este arquivo e a metade B2 -- a unica que da pra escrever sem servidor, porque a outra
/// depende do livro de dominios. Quem junta as duas (e quem oferece a escolha quando existem as
/// duas) e o `Server/GameServer.Refugio.cs`.
/// ================================================================================
///
/// ============================ O QUE ISTO SUBSTITUI, E POR QUE AQUILO TINHA QUE MORRER ============================
/// Havia um `ZonaDeRecuoViva` no servidor que descia `Espaco.PreFeitos()` e devolvia o primeiro
/// planeta VIVO. Ele funcionava, e mesmo assim era um defeito: o destino de quem perdia o berco era
/// **uma posicao numa lista** -- Namek so recebia os desabrigados da Terra porque e a SEGUNDA LINHA
/// de um `yield return`. Media-se a cascata e ela era brutal: Terra morta mandava 10 de 24 racas pra
/// Namek; Terra+Namek mandavam 14 pra Vegeta; e as sagas do `npcs.json` ja destroem Vegeta e Namek
/// sozinhas. Uma linha nova na frente daquela lista mudaria o destino de todo mundo em silencio.
///
/// **AQUI NAO HA LISTA E NAO HA TABELA DE VIZINHOS.** Ha uma REGRA -- *o mundo vivo mais perto de
/// casa* --, e ela le a MESMA fonte que a carta estelar desenha: <see cref="Sistemas.Do"/> e
/// <see cref="SistemaSolar.Planeta"/>. Uma tabela de "quem e vizinho de quem" envelheceria na
/// primeira semente nova; uma medida de distancia nao envelhece nunca.
/// ============================================================================================================
///
/// ============================ POR QUE A BUSCA TEM UM TETO, E ELE E CURTO ============================
/// O universo e infinito e 62,4% das celulas tem sistema, entao *"o mundo vivo mais perto"* SEMPRE
/// existe se a busca puder crescer pra sempre. So que "perto do planeta natal dele" tem significado:
/// uma celula mede <see cref="Sistemas.CelulaPx"/> = 65.536 px = 6,8 min de voo base, e o pre-feito
/// mais proximo da Terra (Makyo_Star) esta a 68,9 min. Com <see cref="CelulasDeBusca"/> = 1 o
/// refugio olha o anel 0 e o anel 1 -- no maximo ~13,6 min de casa, cinco vezes mais perto que o
/// vizinho pre-feito mais proximo. Alem disso ja nao e a vizinhanca de casa: e outro canto do mapa.
///
/// **E o teto e o que faz a opcao B1 e o ultimo recurso serem alcancaveis.** Sem ele a vizinhanca
/// nunca estaria vazia, o ramo "so o dominio existe" nunca rodaria e o ultimo recurso seria codigo
/// morto -- e este projeto ja pagou a conta de guarda que nunca dispara.
/// ================================================================================================
/// </summary>
public static class Refugios
{
	/// <summary>
	/// Raio da busca, em CELULAS de sistema (Chebyshev). 1 = o anel da propria celula mais o 3x3 em
	/// volta. Ver o cabecalho pro porque de ser curto.
	///
	/// **Negativo desliga a busca** e faz a vizinhanca sair VAZIA. Ninguem passa isso em jogo: e a
	/// unica maneira de uma bancada alcancar o ramo "so o dominio existe" e o ultimo recurso contra o
	/// CODIGO DE PRODUCAO, que e a mesma disciplina do `teto` do <see cref="Bercos.ServeDeBerco"/>.
	/// </summary>
	public const int CelulasDeBusca = 1;

	/// <summary>
	/// Quantos mundos a vizinhanca guarda. Tres e o numero de orbitas livres de um sistema ancorado
	/// (<see cref="Sistemas.OrbitasAncoradas"/> menos a do pre-feito) -- ou seja, no caso comum a
	/// vizinhanca inteira cabe, e o corte so morde quando o anel 1 entra na conta.
	///
	/// Ele existe porque o sorteio e uniforme sobre a lista: sem corte, um anel 1 com trinta mundos
	/// daria 3/33 de chance de o corpo ficar perto de casa e 30/33 de ele ir parar na borda do teto.
	/// </summary>
	public const int MundosGuardados = 3;

	internal const ulong SalDoRefugio = 0x7E4F1D0C9A62B531UL;

	/// <summary>
	/// **OS MUNDOS VIVOS MAIS PERTO DE UM PONTO.** Esta e a funcao.
	///
	/// ============================ ANEIS QUE CRESCEM, COM PARADA EXATA ============================
	/// A varredura anda em aneis de Chebyshev a partir da celula da ancora. A parada nao e um chute:
	/// a ancora esta DENTRO da celula 0, entao todo ponto de uma celula do anel `d` esta a pelo menos
	/// `(d-1) * CelulaPx` dela. Assim, terminado o anel `d`, o anel seguinte so pode trazer algo mais
	/// perto se `d * CelulaPx` for menor que a maior distancia ja guardada -- e como uma irma de
	/// orbita fica a ~9.800 px e a celula mede 65.536, o caso comum para no anel 1: 9 celulas, 9
	/// hashes.
	///
	/// **NAO SE ORDENA O UNIVERSO PRA PEGAR O PRIMEIRO.** Guarda-se so o top-N por insercao ordenada
	/// numa lista de tres. O custo nao depende de quantos corpos o anel tem.
	/// ========================================================================================
	///
	/// ============================ E A RESERVA E COLHIDA NA MESMA PASSADA ============================
	/// Um mundo perto pode ser pesado demais pra um recem-nascido (o crivo do
	/// <see cref="Bercos.ServeDeBerco"/>, teto de <see cref="Bercos.GravidadeMaximaDeBerco"/> g). Se
	/// NENHUM dos vizinhos passar, a resposta nao pode ser "lugar nenhum" -- e a mesma decisao que o
	/// <see cref="Bercos.Exilio"/> ja tomou e escreveu: *"mundo pesado e melhor que lugar nenhum, e o
	/// `max` do `race.dm:130-131` acostuma o corpo a gravidade do berco, entao ele sobrevive"*.
	///
	/// As duas listas sao preenchidas na mesma volta; a segunda so e devolvida quando a primeira sai
	/// vazia, e ai <see cref="Arredores.Reserva"/> diz isso em voz alta.
	/// =========================================================================================
	/// </summary>
	/// <param name="ancora">
	/// De onde se mede "perto" -- a posicao do planeta NATAL, e nao a de onde o corpo esta. O pedido
	/// do dono fala do natal, e um jogador que morresse do outro lado da galaxia acabaria "perto"
	/// de um canto que ele nunca viu.
	/// </param>
	/// <param name="existe">
	/// ESTE CORPO AINDA ESTA LA? A destruicao de planeta e estado de servidor e nao cabe no Core --
	/// entao ela entra como pergunta. Quem chama responde `!ZonaMorta(...)`, e a bancada responde o
	/// que quiser sem precisar matar mundo de verdade.
	/// </param>
	/// <param name="teto">O crivo de gravidade. Ver <see cref="Bercos.ServeDeBerco"/> -- so a bancada mexe.</param>
	/// <param name="celulas">Ver <see cref="CelulasDeBusca"/>. Negativo = sem vizinhanca.</param>
	public static Arredores MundosPertoDe(ulong seedDoUniverso, Vec2 ancora,
										   Func<PlanetaNoEspaco, bool> existe,
										   int quantos = MundosGuardados,
										   double teto = Bercos.GravidadeMaximaDeBerco,
										   int celulas = CelulasDeBusca)
	{
		SistemaId c0 = SistemaId.De(ancora);
		var servem = new List<MundoDeRefugio>(quantos + 1);
		var pesados = new List<MundoDeRefugio>(quantos + 1);
		int olhadas = 0;

		for (int d = 0; d <= celulas; d++)
		{
			// ============================ A PARADA EXATA, E O `-1` NAO E ENGANO ============================
			// A ancora esta DENTRO da celula 0 e pode encostar na divisa dela. Entao a menor distancia
			// que uma celula do anel `d` consegue ter da ancora nao e `d * CelulaPx` -- e
			// `(d-1) * CelulaPx`, porque uma celula inteira pode estar "gasta" pela posicao da ancora
			// dentro da propria. Com o `d` cru a varredura pararia UMA VEZ CEDO DEMAIS e poderia
			// devolver o segundo mundo mais perto como se fosse o primeiro -- o tipo de erro que fica
			// verde por anos porque o resultado continua plausivel.
			//
			// Em `d = 1` a conta da zero e o anel 1 e sempre varrido (ele encosta na ancora); em
			// `d = 2` ela ja da 65.536 px, e uma irma de orbita esta a ~9.800 -- e por isso o caso
			// comum custa 9 celulas.
			// ==========================================================================================
			if (servem.Count >= quantos && (d - 1) * Sistemas.CelulaPx >= servem[^1].Distancia) break;

			foreach ((int sx, int sy) in Anel(c0, d))
			{
				olhadas++;
				if (Sistemas.Do(seedDoUniverso, sx, sy) is not { } s) continue;

				for (int k = 0; k < s.Orbitas; k++)
				{
					PlanetaNoEspaco p = s.Planeta(k);
					if (!existe(p)) continue;

					double dx = p.Pos.X - ancora.X, dy = p.Pos.Y - ancora.Y;
					var m = new MundoDeRefugio(p, sx, sy, k, Math.Sqrt(dx * dx + dy * dy),
											   Bercos.ServeDeBerco(p, teto));
					Guardar(m.ServeDeBerco ? servem : pesados, m, quantos);
				}
			}
		}

		bool reserva = servem.Count == 0;
		return new Arredores
		{
			Mundos = reserva ? pesados : servem,
			Reserva = reserva && pesados.Count > 0,
			CelulasOlhadas = olhadas,
			Celula = c0,
		};
	}

	/// <summary>
	/// AS CELULAS DA BORDA de um quadrado de raio Chebyshev `d`. `d = 0` e a propria celula.
	///
	/// Irma da <see cref="Bercos.CelulaNoAnel"/> e pelo mesmo motivo: aritmetica inteira pura, sem
	/// trigonometria -- `Cos`/`Sin` variam de plataforma pra plataforma e as duas pontas deste jogo
	/// tem que chegar na MESMA lista.
	/// </summary>
	private static IEnumerable<(int Sx, int Sy)> Anel(SistemaId c, int d)
	{
		if (d == 0) { yield return (c.Sx, c.Sy); yield break; }

		for (int i = -d; i <= d; i++)
		{
			yield return (c.Sx + i, c.Sy - d);   // aresta de cima
			yield return (c.Sx + i, c.Sy + d);   // aresta de baixo
		}

		// as laterais SEM as quinas, que as duas linhas acima ja deram
		for (int j = -d + 1; j <= d - 1; j++)
		{
			yield return (c.Sx - d, c.Sy + j);
			yield return (c.Sx + d, c.Sy + j);
		}
	}

	/// <summary>
	/// INSERE ORDENADO e corta no `quantos`. Lista de tres: o `Insert` custa menos que ordenar o
	/// universo, e o custo por corpo e constante independentemente de quantos corpos o anel tem.
	/// </summary>
	private static void Guardar(List<MundoDeRefugio> l, MundoDeRefugio m, int quantos)
	{
		int i = 0;
		while (i < l.Count && l[i].Distancia <= m.Distancia) i++;

		if (i >= quantos) return;      // ja ha `quantos` mais perto que este
		l.Insert(i, m);
		if (l.Count > quantos) l.RemoveAt(l.Count - 1);
	}
}
