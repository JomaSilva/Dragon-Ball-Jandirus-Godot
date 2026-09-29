using Jandirus.Core.World;

namespace Jandirus.Core.Combat;

/// <summary>
/// A GEOMETRIA DO FEIXE -- onde a cabeca para quando encosta em alguem, o que e TRONCO, e o que
/// acontece quando um feixe cruza o caminho do outro. Puro, sem servidor e sem Godot.
///
/// ============================ O PEDIDO DO DONO (2026-09-07), COM A FOTO ============================
/// *"ao colidirem a cabeca deles sempre devem ficar se empurrando na colisao, e isso tambem
/// acontece pra quando se chocar com alguem (atualmente a cabeca do beam fica SOBRE a pessoa e nao
/// NA FRENTE dela a empurrando e dando dano)"*; *"caso um jogador encoste no TRONCO de um beam e nao
/// necessariamente na cabeca, o beam vai ser CORTADO e a cabeca nova vai colidir com essa pessoa, e a
/// outra parte continua normalmente"*; *"no caso de 2 beams se CRUZAREM eles nao vao entrar em clash,
/// eles tem que vir de direcoes opostas, e o beam que bater no tronco do outro vai ficar PARADO ate o
/// outro sair do caminho"*.
///
/// Tres perguntas geometricas, e todas moram aqui porque as tres sao a mesma conta com o mesmo
/// numero: a cabeca tem um raio (<see cref="Projetil.RaioDeImpacto"/>) e o corpo tem uma meia
/// largura (<see cref="MeioCorpo"/>). "Encostar" e a soma das duas, em qualquer dos tres casos.
/// ===============================================================================================
///
/// ============================ O QUE E DO DM E O QUE E DO DONO ============================
///   * O CORTE do tronco e do DM: `Crossed(mob/M)` de `objects.dm:156-172` -- o segmento pisado some,
///     o de tras vira cabeca (`B.icon_state = "head"; B.density = 1; walk(B, ...)`) e vai dar o `Bump`
///     no mob; os segmentos da frente seguem viagem porque cada um anda sozinho. Aqui o feixe e um
///     objeto so, entao cortar e PARTIR o objeto em dois (ver `GameServer.Feixe.cs`).
///   * O CRUZAMENTO e do dono, e DIVERGE do DM de proposito: la `Crossed(obj/attack)` compara o poder
///     dos dois e apaga o mais fraco depois de um `sleep` (`objects.dm:173-185`). O dono pediu outra
///     coisa -- o que bate no tronco alheio ESPERA o tronco sair --, e e a que vale.
///   * A CABECA NA FRENTE e do dono; o DM nao tem a pergunta porque la o beam ocupa TILES, e "estar em
///     cima" e "estar na frente" sao o mesmo tile.
/// ==========================================================================================
/// </summary>
public static class Feixe
{
	/// <summary>
	/// A META LARGURA DE UM CORPO, em pixels: a caixa dos pes do <c>MoveRules</c> tem 16 px de largura,
	/// e a cabeca de um feixe encosta na BEIRADA dela, nao no centro.
	/// </summary>
	public const float MeioCorpo = 8f;

	/// <summary>
	/// A QUE DISTANCIA DO CENTRO DO CORPO A CABECA PARA: o raio dela mais a meia largura dele. E a
	/// unica conta de "encostar" deste arquivo, e as tres perguntas a usam.
	/// </summary>
	public const float DistanciaDeContato = Projetil.RaioDeImpacto + MeioCorpo;

	// =====================================================================
	// OS DEFEITOS INJETAVEIS DAS BANCADAS
	// =====================================================================
	/// <summary>DEFEITO INJETADO: a cabeca fica onde encostou, em cima do corpo -- a foto do dono.</summary>
	public static bool CabecaEmCimaDeTeste;

	/// <summary>DEFEITO INJETADO: a cabeca atravessa o tronco alheio em vez de esperar.</summary>
	public static bool AtravessaTroncoDeTeste;

	/// <summary>DEFEITO INJETADO: encostar no tronco nao corta nada.</summary>
	public static bool SemCorteDeTeste;

	/// <summary>
	/// DEFEITO INJETADO: toda cabeca tem o alcance da `Beam3` (16 px), seja qual for a folha e a escala --
	/// o codigo de antes de 2026-09-23, em que o Final Flash (128 px de frente) disputava a 32 px.
	/// </summary>
	public static bool AlcanceFixoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO: duas cabecas de frente que NAO podem disputar se atravessam -- o feixe solto, a
	/// revanche e o devolvido passavam um por dentro do outro ate o tronco alheio os parar.
	/// </summary>
	public static bool AtravessaDeFrenteDeTeste;

	// =====================================================================
	// A CABECA NA FRENTE
	// =====================================================================
	/// <summary>
	/// A QUE DISTANCIA DO CENTRO DO CORPO O CENTRO DESTA CABECA PARA: a frente desenhada dela
	/// (<see cref="AlcanceDaCabeca(Projetil)"/>) mais a meia largura dele. Na `Beam3` de escala 1 e a
	/// <see cref="DistanciaDeContato"/>; num Final Flash (folha de 64 px, escala 4) sao 136 px.
	/// </summary>
	public static float ContatoNoCorpo(Projetil p) => AlcanceDaCabeca(p) + MeioCorpo;

	/// <summary>
	/// ONDE A CABECA DESTE TIRO FICA QUANDO ENCOSTA NESTE CORPO: com a FRENTE na beirada dele -- e ANDANDO SO
	/// PELO EIXO DO PROPRIO FEIXE (2026-09-23).
	///
	/// ============================ POR QUE PELO EIXO, E NAO "NA FRENTE DO CENTRO DELE" ============================
	/// A versao anterior punha a cabeca em `corpo - rumo * contato`: na LINHA DO CORPO. Com o corpo de lado (o
	/// contato de um Final Flash pega quem esta a 130 px da linha dele), a cabeca pulava de lado pra linha do
	/// corpo, a cauda ficava na mao do dono, e o feixe era desenhado TORTO. Agora so a distancia ao longo do
	/// eixo muda: a cabeca recua (ou avanca) ate a frente dela ficar na altura da beirada do corpo.
	///
	/// NO PLANO DESENHADO: a distancia ao longo do eixo e medida entre o corpo e a cabeca como eles aparecem na
	/// tela (cada um subido pela propria altura, <see cref="Subida"/>). Num feixe norte-sul a subida cai no
	/// eixo, e quem paira atirando no chao desenharia a cabeca 12 a 40 px dentro do corpo.
	///
	/// E NUNCA ATRAS DA MAO: a queima-roupa (o corpo mais perto que a mao mais o contato), o lugar "na frente
	/// dele" fica atras da boca do cano -- e o feixe seria desenhado ao contrario. Ali a cabeca para na propria
	/// cauda (<see cref="NaoAtrasDaCauda"/>).
	/// ====================================================================================================
	/// </summary>
	public static Vec2 CabecaNaFrenteDe(Vec2 corpo, float alturaDoCorpo, Projetil p)
	{
		Vec2 d = corpo + Subida(alturaDoCorpo) - NaTela(p);
		float falta = d.X * p.Rumo.X + d.Y * p.Rumo.Y - ContatoNoCorpo(p);
		return NaoAtrasDaCauda(p.Pos + p.Rumo * falta, p);
	}

	/// <summary>
	/// A CABECA NUNCA FICA ATRAS DA PROPRIA CAUDA, no rumo do feixe: um feixe com comprimento negativo e
	/// desenhado ao contrario (o cliente tira o rumo de cabeca - cauda). Devolve a posicao corrigida.
	/// </summary>
	public static Vec2 NaoAtrasDaCauda(Vec2 cabeca, Projetil p)
	{
		Vec2 corpo = cabeca - p.Cauda;
		return corpo.X * p.Rumo.X + corpo.Y * p.Rumo.Y < 0 ? p.Cauda : cabeca;
	}

	// =====================================================================
	// O PLANO DESENHADO -- onde as coisas APARECEM (2026-09-23)
	// =====================================================================
	/// <summary>
	/// QUANTO UMA COISA A ESTA ALTURA SOBE NA TELA: `(0, -altura x Voo.EscalaNaTela)`. E a mesma conta do
	/// corpo (`SubirComOVoo`) e do tiro (`ProjetilDesenhado.SubidaNaTela`).
	/// </summary>
	public static Vec2 Subida(float altura) => new(0, -altura * Voo.EscalaNaTela);

	/// <summary>
	/// A CABECA DESTE RAIO ENCOSTA NESTE CORPO? Uma FAIXA pelo eixo do feixe, e nao um circulo (2026-09-23):
	/// de lado, ate um contato (a frente desenhada + a meia largura do corpo); no eixo, do contato pra frente
	/// ate um contato pra tras. E a mesma regua do plantio (<see cref="CabecaNaFrenteDe"/>): com o circulo, um
	/// corpo 60 px ao lado de um Final Flash so era "encostado" com a cabeca 13 px ALEM do lugar em que o
	/// plantio a poe -- e entre um ciclo de moida e o outro ela andava pra dentro dele.
	///
	/// O LADO E MEDIDO NO CHAO e o EIXO NO PLANO DESENHADO: quem acerta quem continua sendo a regra do chao
	/// (um feixe rasante acerta quem esta em pe na mesma fileira, `Voo.PodeAcertar`); so a distancia pelo
	/// eixo -- que num feixe norte-sul carrega a subida do voo -- e a do desenho.
	/// </summary>
	public static bool EncostaNoCorpo(Projetil p, Vec2 corpo, float alturaDoCorpo)
	{
		float contato = ContatoNoCorpo(p);
		Vec2 chao = corpo - p.Pos;
		float lado = MathF.Abs(chao.X * p.Rumo.Y - chao.Y * p.Rumo.X);
		if (lado > contato) return false;
		Vec2 tela = corpo + Subida(alturaDoCorpo) - NaTela(p);
		float frente = tela.X * p.Rumo.X + tela.Y * p.Rumo.Y;
		return frente <= contato + 0.5f && frente >= -contato;
	}

	/// <summary>Onde a cabeca deste tiro APARECE: a posicao dele subida pela altura dele.</summary>
	public static Vec2 NaTela(Projetil p) => p.Pos + Subida(p.Altitude);

	/// <summary>
	/// DUAS CABECAS DESTAS ALTURAS PODEM SE ENCOSTAR? A regra do voo pra acertar (`Voo.PodeAcertar`) nos DOIS
	/// sentidos: quem paira rasante alcanca o chao, e um feixe rasante e um feixe no chao se encontram -- sao
	/// desenhados a 12-40 px um do outro, e "andares diferentes" os deixaria se atravessar. Dois andares acima
	/// ja nao se tocam, como nao se acertam.
	/// </summary>
	public static bool PodemSeTocar(float alturaA, float alturaB)
	{
		int a = Voo.Andar(alturaA), b = Voo.Andar(alturaB);
		return Voo.PodeAcertar(a, b) || Voo.PodeAcertar(b, a);
	}

	/// <summary>
	/// A MEIA ESPESSURA DESENHADA DO TRONCO DESTE TIRO: a da folha (<see cref="ArteDeProjetil.MeiaEspessuraDoTronco"/>)
	/// vezes a escala. O tronco do Final Flash e desenhado 108 px pra cada lado do eixo.
	/// </summary>
	public static float MeiaEspessuraDoTronco(Projetil p)
		=> AlcanceFixoDeTeste
			? Projetil.RaioDeImpacto / 4f
			: ArteDeProjetil.MeiaEspessuraDoTronco(p.Arte) * (float)(p.EscalaVisual > 0 ? p.EscalaVisual : 1);

	/// <summary>
	/// O PONTO DO IMPACTO de um tiro neste corpo -- onde a faisca estoura (dono, 2026-09-23: *"o efeito de
	/// hit tem que ser na CABECA do beam e nao no meio dele"*). Raio: a frente da cabeca, que e a beirada
	/// do corpo (a cabeca e plantada com a frente ali). Bola e teleguiado: onde a bola esta.
	/// </summary>
	public static Vec2 PontoDoImpacto(Projetil p, Vec2 corpo)
		=> p.Tipo == TipoDeProjetil.Beam ? corpo - p.Rumo * MeioCorpo : p.Pos;

	// =====================================================================
	// O ALCANCE DA CABECA -- o quanto ela avanca a frente do centro, DESENHADA
	// =====================================================================
	/// <summary>
	/// QUANTO A CABECA DESTE TIRO AVANCA A FRENTE DA POSICAO DELA, em pixel de mundo: a frente da arte
	/// (<see cref="ArteDeProjetil.FrenteDaCabeca"/>) vezes a escala do tiro (o `A.transform *= wavemult`
	/// do DM, `beams.dm:149`). E a medida que diz onde a FRENTE desenhada esta -- e "encostar" (outra
	/// cabeca, um corpo) e sempre a frente encostando, nunca o centro.
	/// </summary>
	public static float AlcanceDaCabeca(ArteDeKi arte, double escala)
		=> AlcanceFixoDeTeste
			? Projetil.RaioDeImpacto
			: ArteDeProjetil.FrenteDaCabeca(arte) * (float)(escala > 0 ? escala : 1);

	/// <summary>O alcance da cabeca deste projetil. Bola e teleguiado continuam no raio de impacto.</summary>
	public static float AlcanceDaCabeca(Projetil p)
		=> p.Tipo == TipoDeProjetil.Beam ? AlcanceDaCabeca(p.Arte, p.EscalaVisual) : Projetil.RaioDeImpacto;

	/// <summary>
	/// A DISTANCIA ENTRE OS CENTROS DE DUAS CABECAS QUE SE TOCAM FRENTE COM FRENTE: a soma das duas frentes.
	/// E onde a disputa as mantem (`MoverOEncontro`) e onde o empurrao sem disputa as deixa.
	/// </summary>
	public static float Contato(Projetil a, Projetil b) => AlcanceDaCabeca(a) + AlcanceDaCabeca(b);

	/// <summary>
	/// A DISTANCIA EM QUE UMA CABECA "VE" A OUTRA -- o gatilho da disputa, a espera no cruzamento, o empurrao.
	/// E o <see cref="Contato"/>, com um PISO de dois raios de impacto (um tile): a cabeca anda de 16 em 16 px
	/// por sub-passo, e um gatilho menor que o passo deixaria duas pontas finas (a broca do Makkankosappo tem
	/// frente ZERO) se cruzarem entre dois sub-passos sem se ver. O piso so detecta mais cedo; onde elas
	/// PARAM continua sendo o contato.
	/// </summary>
	public static float Toque(Projetil a, Projetil b) => MathF.Max(Contato(a, b), 2f * Projetil.RaioDeImpacto);

	/// <summary>
	/// A LARGURA DA FAIXA EM QUE DUAS CABECAS DE FRENTE SE VEEM, de lado: um tile e meio, ou a soma das
	/// frentes quando elas sao maiores (dois Final Flash se veem a 256 px de lado, que e o que o desenho
	/// deles cobre).
	///
	/// ============================ O `range(1)` DO DM, E POR QUE UM CIRCULO NAO BASTAVA ============================
	/// O gatilho era um CIRCULO de 32 px entre os centros. Dois duelistas a um tile de lado (os feixes
	/// em fileiras vizinhas) nunca entravam nele -- e quando entravam por pouco (31 px), o sub-passo de
	/// 16 px podia pular a janela. O DM escreveu o `range(1)` (`objects.dm:248`, a caixa 3x3 de tiles em
	/// volta da cabeca) justamente por isso: o comentario ao lado diz que feixes retos em fileiras
	/// vizinhas se atravessavam sem nunca colidir (`:242-245`). Um tile de lado e vizinho; o meio tile a
	/// mais e o arredondamento de posicao em pixel pra tile.
	/// ======================================================================================================
	/// </summary>
	public static float FaixaDeFrente(Projetil a, Projetil b)
		=> MathF.Max(1.5f * ZoneCollision.TileSize, Contato(a, b));

	/// <summary>
	/// A CABECA DE <paramref name="p"/>, indo pra <paramref name="nova"/>, ENCONTRA DE FRENTE a cabeca de
	/// <paramref name="q"/>? Os dois vem um contra o outro (<see cref="VemContra"/>), a de `q` esta na
	/// <see cref="FaixaDeFrente"/> de lado, e no eixo ela esta a menos de um <see cref="Toque"/> na frente
	/// -- ou ja ATRAS, ate um contato: a cabeca que nasceu por cima da outra (o contra-feixe disparado
	/// tarde, a queima-roupa) tambem se ve, e a disputa as desencavala.
	///
	/// E A PERGUNTA UNICA do gatilho da disputa e do empurrao sem disputa: os dois perguntando a mesma
	/// coisa e o que impede um caso de cair no vao entre as duas regras.
	/// </summary>
	public static bool DeFrenteNoCaminho(Projetil p, Vec2 nova, Projetil q)
	{
		if (!VemContra(p.Rumo, q.Rumo)) return false;
		if (!PodemSeTocar(p.Altitude, q.Altitude)) return false;
		// NO PLANO DESENHADO: cada cabeca subida pela propria altura -- e ali que elas se encostam ou nao.
		Vec2 d = NaTela(q) - (nova + Subida(p.Altitude));
		float frente = d.X * p.Rumo.X + d.Y * p.Rumo.Y;
		float lado = MathF.Abs(d.X * p.Rumo.Y - d.Y * p.Rumo.X);
		return lado <= FaixaDeFrente(p, q) && frente <= Toque(p, q) && frente >= -Contato(p, q);
	}

	// =====================================================================
	// DE FRENTE, SEM DISPUTA
	// =====================================================================
	/// <summary>O que uma cabeca faz ao encontrar outra DE FRENTE quando a disputa nao pode comecar.</summary>
	public enum DeFrente : byte
	{
		/// <summary>Eu paro com a frente encostada na dele.</summary>
		Espera,

		/// <summary>Eu sigo e EMPURRO a cabeca dele de volta, encolhendo o feixe dele.</summary>
		Empurra,
	}

	/// <summary>
	/// ============================ DUAS CABECAS DE FRENTE NUNCA SE ATRAVESSAM ============================
	/// A disputa so comeca com OS DOIS feixes alimentados pelos donos (`bcl_try_start`,
	/// `BeamClash.dm:68-83`: `A.beaming &amp;&amp; B.beaming`). Fora dela -- o feixe que o dono ja SOLTOU, a
	/// parte de la de um feixe cortado, o ataque DEVOLVIDO pela guarda, o dono preso noutro embate -- o
	/// port nao tinha regra nenhuma pra cabeca contra cabeca, e as duas passavam uma por dentro da outra
	/// ate o tronco alheio as parar (medido: 96 px de sobreposicao, `--embatekiteste` 1d). E o "elas
	/// ainda estao se sobrepondo as vezes" do dono (2026-09-23).
	///
	/// A REGRA: quem e ALIMENTADO empurra quem nao e -- a cabeca solta recua na frente da alimentada, e o
	/// feixe dela encolhe ate sumir (e o `push_phase` do DM engolindo os segmentos de quem perdeu, sem
	/// cabo de guerra porque do outro lado nao ha ninguem apertando). Nos outros casos (os dois soltos, os
	/// dois alimentados sem poder disputar, eu solto contra ele alimentado) EU ESPERO encostado: se eu sou
	/// solto, a minha cauda continua vindo e o meu feixe se esvazia ali; se ele e alimentado, e ELE quem
	/// me empurra no passo dele.
	///
	/// DIVERGENCIA DECLARADA DO DM: la dois feixes que nao disputam caem no `Crossed(obj/attack)`
	/// (`objects.dm:173-185`), que compara o poder e APAGA o mais fraco. O dono pediu *"sempre devem ficar
	/// se empurrando na colisao"* -- empurrar e encostar, e nao sumir um de dentro do outro.
	/// ==================================================================================================
	/// </summary>
	public static DeFrente SemDisputa(bool euAlimentado, bool eleAlimentado)
		=> euAlimentado && !eleAlimentado ? DeFrente.Empurra : DeFrente.Espera;

	// =====================================================================
	// DE FRENTE OU CRUZANDO
	// =====================================================================
	/// <summary>
	/// O OUTRO VEM CONTRA MIM? O rumo dele dentro de 45 graus do oposto do meu -- `R.dir != opp &amp;&amp;
	/// R.dir != turn(opp,45) &amp;&amp; R.dir != turn(opp,-45)` do gatilho por proximidade (`objects.dm:253`).
	/// `cos(135) = -0,707`. Fora disso os dois se CRUZAM, e cruzar nao e disputa.
	/// </summary>
	public static bool VemContra(Vec2 meuRumo, Vec2 rumoDele)
		=> meuRumo.X * rumoDele.X + meuRumo.Y * rumoDele.Y <= -0.7f;

	/// <summary>
	/// DUAS CABECAS QUE SE CRUZAM (sem vir de frente): quem espera e a mais fraca; empatadas, espera
	/// quem chegou por ultimo -- que e quem esta perguntando.
	/// </summary>
	public static bool EsperaNoCruzamento(double meuPoder, double poderDele) => meuPoder <= poderDele;

	// =====================================================================
	// O TRONCO
	// =====================================================================
	/// <summary>
	/// O TRONCO DE UM FEIXE: da cauda ate onde a cabeca deixa de ser cabeca. Devolve falso quando nao
	/// ha tronco (feixe curto demais, ou cauda ja em cima da cabeca).
	///
	/// A CABECA E MAIS COMPRIDA QUE O RAIO DELA, de proposito: quem esta a menos do contato da cabeca
	/// (a frente dela mais a meia largura do corpo, <paramref name="alcance"/> + <see cref="MeioCorpo"/>) +
	/// <see cref="Projetil.RaioDeImpacto"/> e assunto da CABECA (o corpo que ela ja esta empurrando fica
	/// exatamente no contato do centro dela, e nao pode ser lido como "alguem encostou no tronco" e cortar
	/// o proprio feixe que o empurra). Um corpo nessa faixa que ainda nao foi acertado e alcancado pela
	/// cabeca no proximo sub-passo -- ela anda 16 px por vez. Na `Beam3` de escala 1 sao 40 px.
	/// </summary>
	/// <param name="alcance">A frente desenhada DESTA cabeca (<see cref="AlcanceDaCabeca(Projetil)"/>).</param>
	public static bool Tronco(Vec2 cauda, Vec2 cabeca, Vec2 rumo, float alcance, out Vec2 fim)
	{
		fim = cabeca - rumo * (alcance + MeioCorpo + Projetil.RaioDeImpacto);
		Vec2 eixo = fim - cauda;
		return eixo.LengthSquared > 1f && eixo.X * rumo.X + eixo.Y * rumo.Y > 0;
	}

	/// <summary>
	/// ESTE PONTO ENCOSTA NO TRONCO? Distancia do ponto ao segmento do tronco menor ou igual ao raio.
	/// Devolve a projecao dele no eixo do feixe -- o lugar do corte.
	/// </summary>
	public static bool EncostaNoTronco(Vec2 cauda, Vec2 cabeca, Vec2 rumo, float alcance, Vec2 ponto, float raio,
									   out Vec2 projecao)
	{
		projecao = default;
		if (!Tronco(cauda, cabeca, rumo, alcance, out Vec2 fim)) return false;

		Vec2 eixo = fim - cauda;
		Vec2 ate = ponto - cauda;
		float t = (ate.X * eixo.X + ate.Y * eixo.Y) / eixo.LengthSquared;
		if (t < 0f || t > 1f) return false;

		projecao = cauda + eixo * t;
		return (ponto - projecao).LengthSquared <= raio * raio;
	}
}
